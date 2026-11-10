// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Windows;

public partial class LineupEditorWindow : Window
{
    private readonly LineupEditorViewModel _vm;
    private readonly IReadOnlyList<Strategy> _strategies;

    /// <summary>Set to the saved Lineup if user clicked Save, else null.</summary>
    public Lineup? Result { get; private set; }

    public LineupEditorWindow(LineupEditorViewModel vm, IReadOnlyList<Strategy> strategies)
    {
        _vm = vm;
        _strategies = strategies;
        InitializeComponent();
        foreach (var phase in Enum.GetValues<LineupPhase>())
        {
            var button = new Button { Content = phase.ToString(), Style = (Style)FindResource("ChipButton"), Padding = new Thickness(16, 4, 16, 4) };
            button.Click += (_, _) =>
            {
                _vm.SelectPhase(phase);
                RefreshEditor();
            };
            PhaseTabs.Children.Add(button);
        }
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LineupEditorViewModel.OpeningStrategyId))
                RefreshOpeningStrategy();
        };
        RefreshEditor();
    }

    private void RefreshEditor()
    {
        LineupNameBox.Text = _vm.Name;
        foreach (Button button in PhaseTabs.Children)
            button.Tag = (string)button.Content == _vm.CurrentPhase.ToString() ? "active" : null;
        SyncUpgradeBoxes();
        RefreshOpeningStrategy();
        BuildSlotCards();
        RefreshCovenantPanel();
    }

    // ── 升本计划 ────────────────────────────────────────────────────────────

    private bool _syncingUpgradeBoxes;
    private bool _upgradeInputInvalid;

    /// <summary>2–6 本的起点输入框，索引 0 对应 2 本。</summary>
    private TextBox[] UpgradeFromBoxes =>
        [UpgradeFromBox2, UpgradeFromBox3, UpgradeFromBox4, UpgradeFromBox5, UpgradeFromBox6];

    /// <summary>2–6 本的终点输入框，与 <see cref="UpgradeFromBoxes"/> 一一对应。</summary>
    private TextBox[] UpgradeToBoxes =>
        [UpgradeToBox2, UpgradeToBox3, UpgradeToBox4, UpgradeToBox5, UpgradeToBox6];

    private void SyncUpgradeBoxes()
    {
        var from = _vm.UpgradeFromArray;
        var to = _vm.UpgradeToArray;
        var fromBoxes = UpgradeFromBoxes;
        var toBoxes = UpgradeToBoxes;

        _syncingUpgradeBoxes = true;
        try
        {
            for (var i = 0; i < fromBoxes.Length; i++)
            {
                // 数组索引 0 是 1 本（开局），输入框从索引 1 开始。
                fromBoxes[i].Text = from[i + 1]?.ToString() ?? "";
                toBoxes[i].Text = to[i + 1]?.ToString() ?? "";
            }
            // 勾上「看情况自行判断升本」= 不提醒，所以和 UpgradeReminderEnabled 相反。
            UpgradeReminderToggle.IsChecked = !_vm.UpgradeReminderEnabled;
            _upgradeInputInvalid = false;
        }
        finally
        {
            _syncingUpgradeBoxes = false;
        }

        UpdateUpgradeHint();
    }

    private void OnUpgradeRoundChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUpgradeBoxes) return;
        ReadUpgradeBoxes();
        UpdateUpgradeHint();
    }

    private void ReadUpgradeBoxes()
    {
        var from = new int?[UpgradeSchedule.LevelCount];
        var to = new int?[UpgradeSchedule.LevelCount];
        var invalid = false;

        var fromBoxes = UpgradeFromBoxes;
        var toBoxes = UpgradeToBoxes;
        for (var i = 0; i < fromBoxes.Length; i++)
        {
            from[i + 1] = ParseRound(fromBoxes[i].Text, out var fromOk);
            to[i + 1] = ParseRound(toBoxes[i].Text, out var toOk);
            if (!fromOk || !toOk) invalid = true;
        }

        _vm.UpgradeFrom2 = from[1];
        _vm.UpgradeTo2 = to[1];
        _vm.UpgradeFrom3 = from[2];
        _vm.UpgradeTo3 = to[2];
        _vm.UpgradeFrom4 = from[3];
        _vm.UpgradeTo4 = to[3];
        _vm.UpgradeFrom5 = from[4];
        _vm.UpgradeTo5 = to[4];
        _vm.UpgradeFrom6 = from[5];
        _vm.UpgradeTo6 = to[5];
        _upgradeInputInvalid = invalid;
    }

    private void OnUpgradeReminderToggled(object sender, RoutedEventArgs e)
    {
        if (_syncingUpgradeBoxes) return;
        // 勾上「自行判断」= 关掉提醒。
        _vm.UpgradeReminderEnabled = UpgradeReminderToggle.IsChecked != true;
        UpdateUpgradeHint();
    }

    /// <summary>空文本表示「这一本不设区间」；其余必须是 1–30 的整数，否则 ok=false。</summary>
    private static int? ParseRound(string? text, out bool ok)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ok = true;
            return null;
        }

        if (int.TryParse(text, out var value)
            && value >= UpgradeSchedule.MinRound && value <= UpgradeSchedule.MaxRound)
        {
            ok = true;
            return value;
        }

        ok = false;
        return null;
    }

    private void UpdateUpgradeHint()
    {
        var brushKey = "TextSecondaryBrush";
        var schedule = _vm.NormalizedSchedule;
        string text;

        if (_upgradeInputInvalid)
        {
            text = $"只能填 {UpgradeSchedule.MinRound}–{UpgradeSchedule.MaxRound} 的回合数";
            brushKey = "WarningTextBrush";
        }
        else if (schedule.HasOrderingIssue)
        {
            text = $"计划：{schedule.Describe()}（有的本比前一本还早，检查一下）";
            brushKey = "WarningTextBrush";
        }
        else if (!schedule.IsEmpty)
        {
            text = _vm.UpgradeReminderEnabled
                ? $"计划：{schedule.Describe()}"
                : $"自行判断（计划：{schedule.Describe()}，不提醒）";
        }
        else
        {
            text = "还没设区间，不会提醒";
        }

        UpgradePlanHint.Text = text;
        UpgradePlanHint.Foreground = (Brush)FindResource(brushKey);
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnLineupNameChanged(object sender, TextChangedEventArgs e)
    {
        _vm.Name = LineupNameBox.Text;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ReadUpgradeBoxes();
        UpdateUpgradeHint();
        if (_upgradeInputInvalid)
        {
            MessageBox.Show(this, $"升本计划里的回合要填 {UpgradeSchedule.MinRound}–{UpgradeSchedule.MaxRound} 的整数，"
                + "哪一本不想提醒就整行留空。", "升本计划", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = _vm.ToLineup();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "确定恢复到本次打开编辑器时的状态？本次未保存的修改将被撤销。",
                "确认恢复", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        _vm.Reset();
        RefreshEditor();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"确定清空“{_vm.CurrentPhase}”页的干员、装备和标签？阵容名称、开局策略和升本计划会保留，保存前不会改动已保存阵容。",
                "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        _vm.Clear();
        RefreshEditor();
    }

    // ── 开局策略 ────────────────────────────────────────────────────────────

    private Strategy? CurrentOpeningStrategy =>
        _strategies.FirstOrDefault(s => s.Id == _vm.OpeningStrategyId);

    private void OnPickOpeningStrategy(object sender, RoutedEventArgs e) => PickOpeningStrategy();

    private void RefreshOpeningStrategy()
    {
        OpeningStrategyHost.Children.Clear();
        var strategy = CurrentOpeningStrategy;
        var picked = _vm.OpeningStrategyId.Length > 0;
        OpeningStrategyPickButton.Content = picked ? "更换" : "选择";

        if (!picked)
        {
            OpeningStrategyHost.Children.Add(new TextBlock
            {
                Text = "还没选。开局时选定后记在这里，方便对局里照着打。",
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });
            return;
        }

        var row = new DockPanel { LastChildFill = true };

        var portrait = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(4), ClipToBounds = true,
            Background = (Brush)FindResource("NeutralHoverBrush"), Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 9, 0),
        };
        if (strategy is not null)
        {
            portrait.Child = new Image
            {
                Source = (ImageSource?)((Converters.LocalPathToImageConverter)FindResource("PathToImage")).Convert(
                    strategy.IconPath, typeof(ImageSource), null!, System.Globalization.CultureInfo.CurrentCulture),
                Stretch = Stretch.UniformToFill,
            };
        }
        portrait.MouseLeftButtonUp += (_, _) => PickOpeningStrategy();
        DockPanel.SetDock(portrait, Dock.Left);
        row.Children.Add(portrait);

        var remove = new Button
        {
            Content = "✕", FontSize = 11, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent, Foreground = (Brush)FindResource("TextSecondaryBrush"),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "清空开局策略",
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Click += (_, _) =>
        {
            _vm.OpeningStrategyId = string.Empty;
            RefreshCovenantPanel();
        };

        var details = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = strategy?.Name ?? _vm.OpeningStrategyId, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (strategy is not null)
        {
            var hp = new Border
            {
                Background = (Brush)FindResource("AccentTintBrush"), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            hp.Child = new TextBlock
            {
                Text = $"初始生命值 {strategy.InitialHp}", FontSize = 10,
                Foreground = (Brush)FindResource("AccentBrush"),
            };
            titleRow.Children.Add(hp);
        }
        titleRow.Children.Add(remove);
        details.Children.Add(titleRow);
        details.Children.Add(new TextBlock
        {
            Text = strategy is null ? "资料未收录（来自别的版本或分享）" : strategy.Initiator,
            FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });
        if (strategy is not null && strategy.Description.Length > 0)
            details.Children.Add(new TextBlock
            {
                Text = strategy.Description, TextWrapping = TextWrapping.Wrap, MaxWidth = 520,
                TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 11, Margin = new Thickness(0, 5, 0, 0),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });
        row.Children.Add(details);
        OpeningStrategyHost.Children.Add(row);
    }

    private void PickOpeningStrategy()
    {
        var dialog = new SimplePickerDialog("选择开局策略",
            _strategies.Select(s => new SimplePickerDialog.PickerItem(
                s.Id, $"{s.Name} · {s.Initiator}", s.IconPath, null,
                $"{s.Name} · {s.Initiator}\n初始生命值：{s.InitialHp}\n{s.Description}")),
            _vm.OpeningStrategyId, "搜索策略名或发起人 · 单击选择") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _vm.OpeningStrategyId = dialog.Result ?? string.Empty;
        RefreshOpeningStrategy();
        RefreshCovenantPanel();
    }

    // ── Slot card construction ──────────────────────────────────────────────

    private readonly List<Border> _slotCards = [];

    private void BuildSlotCards()
    {
        _slotCards.Clear();
        SlotsHost.Items.Clear();
        for (int i = 0; i < _vm.Slots.Count; i++)
        {
            var card = CreateSlotCard(i);
            _slotCards.Add(card);
            SlotsHost.Items.Add(card);
        }
    }

    private Border CreateSlotCard(int slotIndex)
    {
        var border = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("CardBackgroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Tag = slotIndex,
        };
        RefreshSlotCard(border, slotIndex);
        return border;
    }

    private void RefreshSlotCard(Border border, int slotIndex)
    {
        var slot = _vm.Slots[slotIndex];
        var stack = new StackPanel();

        // Header
        var headerDock = new DockPanel { LastChildFill = true };

        if (string.IsNullOrEmpty(slot.OperatorName))
        {
            var pickBtn = new Button
            {
                Content = "+ 选择干员",
                Style = (Style)FindResource("EditorSecondaryButton"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 14, 8, 14),
                FontSize = 12,
            };
            pickBtn.Click += (_, _) => PickOperator(slotIndex);
            stack.Children.Add(pickBtn);
            border.Child = stack;
            return;
        }

        // Filled: portrait + name + remove button
        var iconBorder = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("NeutralHoverBrush"),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
        };
        var img = new Image { Stretch = Stretch.UniformToFill };
        TryLoadOperatorIcon(slot.OperatorName, img);
        iconBorder.Child = img;
        iconBorder.MouseLeftButtonUp += (_, _) => PickOperator(slotIndex);
        DockPanel.SetDock(iconBorder, Dock.Left);
        headerDock.Children.Add(iconBorder);

        var rightStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };

        var nameRow = new DockPanel();
        var removeBtn = new Button
        {
            Content = "✕",
            FontSize = 11,
            Padding = new Thickness(6, 1, 6, 1),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "清空该槽位",
        };
        removeBtn.Click += (_, _) =>
        {
            _vm.SetSlotOperator(slotIndex, "");
            RefreshSlotCard(border, slotIndex);
            RefreshCovenantPanel();
        };
        DockPanel.SetDock(removeBtn, Dock.Right);
        nameRow.Children.Add(removeBtn);

        var nameText = new TextBlock
        {
            Text = slot.OperatorName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        nameRow.Children.Add(nameText);
        rightStack.Children.Add(nameRow);

        // Equipment chips (2 slots)
        var equipPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        for (int slotI = 0; slotI < LineupEditorViewModel.MaxEquipPerSlot; slotI++)
        {
            string? eqName = slotI < slot.Equipments.Count ? slot.Equipments[slotI] : null;
            var chip = new Button
            {
                Content = eqName ?? "+ 装备",
                Style = (Style)FindResource("ChipButton"),
                ToolTip = eqName ?? "添加装备",
            };
            int captured = slotI;
            chip.Click += (_, _) => PickEquipment(slotIndex, captured);
            if (eqName is not null)
            {
                var isCore = slot.CoreEquipments.Contains(eqName);
                chip.Tag = isCore ? "core" : "active";
                if (isCore)
                {
                    chip.Content = $"★ {eqName}";
                    chip.ToolTip = $"核心装备：{eqName}";
                }
            }
            equipPanel.Children.Add(chip);
        }
        rightStack.Children.Add(equipPanel);

        headerDock.Children.Add(rightStack);
        stack.Children.Add(headerDock);

        // Tags
        var tagsLabel = new TextBlock
        {
            Text = "标签",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 8, 0, 2),
        };
        stack.Children.Add(tagsLabel);
        var tagPanel = new WrapPanel();
        foreach (LineupTag tag in Enum.GetValues<LineupTag>().OrderBy(tag => tag != LineupTag.阵容核心))
        {
            var chip = new Button
            {
                Content = tag.ToString(),
                Style = (Style)FindResource("ChipButton"),
            };
            if (slot.Tags.Contains(tag)) chip.Tag = tag == LineupTag.阵容核心 ? "core" : "active";
            chip.Click += (_, _) =>
            {
                _vm.ToggleSlotTag(slotIndex, tag);
                RefreshSlotCard(border, slotIndex);
                RefreshCovenantPanel();
            };
            tagPanel.Children.Add(chip);
        }
        stack.Children.Add(tagPanel);

        border.Child = stack;
    }

    private void PickOperator(int slotIndex)
    {
        var current = _vm.Slots[slotIndex].OperatorName;
        var dlg = new OperatorPickerWindow(_vm.AllOperators, current)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() == true)
        {
            _vm.SetSlotOperator(slotIndex, dlg.Result ?? "");
            RefreshSlotCard(_slotCards[slotIndex], slotIndex);
            RefreshCovenantPanel();
        }
    }

    private void PickEquipment(int slotIndex, int equipIndex)
    {
        var slot = _vm.Slots[slotIndex];
        if (string.IsNullOrEmpty(slot.OperatorName)) return;

        var current = equipIndex < slot.Equipments.Count ? slot.Equipments[equipIndex] : null;

        var dlg = new SimplePickerDialog(_vm.AllEquipment, current,
            current is not null && slot.CoreEquipments.Contains(current))
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true) return;

        var picked = dlg.Result;
        if (picked is null) return;

        _vm.SetSlotEquipment(slotIndex, equipIndex, picked, dlg.IsCoreEquipment);

        RefreshSlotCard(_slotCards[slotIndex], slotIndex);
        RefreshCovenantPanel();
    }

    private void TryLoadOperatorIcon(string opName, Image image)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "data", "icons", "operators", $"{opName}.png");
        if (!File.Exists(iconPath)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            image.Source = bmp;
        }
        catch
        {
            // ignore – just leave the placeholder background visible
        }
    }

    // ── Covenant panel ──────────────────────────────────────────────────────

    private void RefreshCovenantPanel()
    {
        ActivatedSummaryText.Text = $"激活 {_vm.ActivatedCount} 个盟约 · 已选 {_vm.FilledSlotCount}/9 干员";
        // The ItemsControl re-binds against ActiveStats automatically.
        CovenantStatsList.ItemsSource = null;
        CovenantStatsList.ItemsSource = _vm.ActiveStats;
        EditorHint.Text = $"正在编辑：{_vm.CurrentPhase} · 列表预览与追踪使用后期阵容";
    }

    private void OnCovenantRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not bool activated) return;
        if (activated)
        {
            b.SetResourceReference(Border.BackgroundProperty, "AccentHoverBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            b.BorderThickness = new Thickness(1);
        }
        else
        {
            b.SetResourceReference(Border.BackgroundProperty, "NeutralTintBrush");
            b.BorderThickness = new Thickness(0);
        }
    }
}
