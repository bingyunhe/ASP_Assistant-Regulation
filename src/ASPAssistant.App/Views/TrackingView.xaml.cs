// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class TrackingView : UserControl
{
    public event Action<string>? RemoveTrackingRequested;
    public event Action<string>? ToggleAcquiredRequested;
    public event Action? ClearAllTrackingRequested;
    public static readonly DependencyProperty OperatorDatabaseProperty = DependencyProperty.Register(
        nameof(OperatorDatabase), typeof(IReadOnlyDictionary<string, Operator>), typeof(TrackingView),
        new PropertyMetadata(new Dictionary<string, Operator>()));

    public IReadOnlyDictionary<string, Operator> OperatorDatabase
        => (IReadOnlyDictionary<string, Operator>)GetValue(OperatorDatabaseProperty);

    public static readonly DependencyProperty EquipmentDatabaseProperty = DependencyProperty.Register(
        nameof(EquipmentDatabase), typeof(IReadOnlyDictionary<string, Equipment>), typeof(TrackingView),
        new PropertyMetadata(new Dictionary<string, Equipment>()));

    public IReadOnlyDictionary<string, Equipment> EquipmentDatabase
        => (IReadOnlyDictionary<string, Equipment>)GetValue(EquipmentDatabaseProperty);
    private TrackingViewModel? _vm;
    private INotifyCollectionChanged? _operators;
    private INotifyCollectionChanged? _equipment;
    private CovenantCalculator? _calculator;
    public static readonly DependencyProperty EquipmentRulesProperty = DependencyProperty.Register(
        nameof(EquipmentRules), typeof(LineupEquipmentService), typeof(TrackingView));
    public LineupEquipmentService EquipmentRules => (LineupEquipmentService)GetValue(EquipmentRulesProperty);

    public TrackingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as TrackingViewModel);
    }

    public void SetOperators(IReadOnlyList<Operator> operators)
    {
        SetValue(OperatorDatabaseProperty, operators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First()));
        RefreshSummary();
    }

    public void SetCovenantCalculator(CovenantCalculator calculator)
    {
        _calculator = calculator;
        SetValue(EquipmentRulesProperty, calculator.EquipmentRules);
        RefreshSummary();
    }

    private void Attach(TrackingViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        HookCollections();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrackingViewModel.TrackedOperators) or nameof(TrackingViewModel.TrackedEquipment))
            HookCollections();
        else if (e.PropertyName is nameof(TrackingViewModel.TerminalLevel) or nameof(TrackingViewModel.TerminalLevelText) or nameof(TrackingViewModel.OperatorProgress))
            RefreshSummary();
    }

    private void HookCollections()
    {
        if (_operators is not null) _operators.CollectionChanged -= OnCollectionChanged;
        if (_equipment is not null) _equipment.CollectionChanged -= OnCollectionChanged;
        _operators = _vm?.TrackedOperators;
        _equipment = _vm?.TrackedEquipment;
        if (_operators is not null) _operators.CollectionChanged += OnCollectionChanged;
        if (_equipment is not null) _equipment.CollectionChanged += OnCollectionChanged;
        RefreshSummary();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSummary();

    /// <summary>Updates the empty state and the covenant tally of tracked operators.</summary>
    private void RefreshSummary()
    {
        var operators = _vm?.TrackedOperators.ToList() ?? [];
        var equipment = _vm?.TrackedEquipment.ToList() ?? [];
        var all = operators.Concat(equipment).Where(e => !e.IsExpired).ToList();
        var filter = CompletionFilterList.SelectedIndex;
        bool Match(TrackingEntry entry) => filter == 2 || entry.IsAcquired == (filter == 1);
        var equipmentShown = equipment.Where(Match).ToList();
        EquipmentCells.ItemsSource = equipmentShown;
        EquipmentCount.Text = $"{equipmentShown.Count} 件";
        ProgressSummary.Text = $"待完成 {all.Count(e => !e.IsAcquired)} · 已完成 {all.Count(e => e.IsAcquired)}";
        ClearAllButton.Visibility = operators.Count + equipment.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var lineup = new Lineup
        {
            Slots = operators.Where(e => e.Role == TrackingRole.Final).Select(e => new LineupSlot
            {
                OperatorName = e.Name, Equipments = e.Equipments, IsElite = e.IsElite
            }).ToList()
        };
        var tally = (_calculator?.Compute(lineup) ?? []).Where(s => s.Count > 0)
            .OrderByDescending(s => s.BondType == "SEASON").ThenByDescending(s => s.Count)
            .Select(s => new TrackedCovenantChip(s.Name, s.Count, s.BondType == "SEASON")).ToList();
        TrackedCovenantChips.ItemsSource = tally;
        TrackedCovenantChips.Visibility = tally.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var db = OperatorDatabase;
        var rules = EquipmentRules;
        var cells = operators.Where(e => !e.IsExpired && Match(e)).Select(e =>
        {
            var cell = TrackedCell.From(e, db.GetValueOrDefault(e.Name), rules);
            return cell with { Progress = _vm!.OperatorProgress(e), Sub = $"目标：{(e.IsElite ? "精锐" : "获取")} · {cell.Sub}" };
        }).ToList();
        Fill(FinalSection, FinalCells, FinalCount, cells.Where(c => c.Kind == TrackedCellKind.Final).ToList());
        Fill(TransitionSection, TransitionCells, TransitionCount, cells.Where(c => c.Kind == TrackedCellKind.Transition).ToList());
        var past = operators.Where(e => e.IsExpired).Select(e => TrackedCell.From(e, db.GetValueOrDefault(e.Name), rules) with { Progress = "已过期" }).ToList();
        Fill(PastSection, PastCells, PastCount, past);
        var empty = cells.Count + equipmentShown.Count == 0 && (filter != 2 || past.Count == 0);
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Title = operators.Count + equipment.Count == 0 ? "还没有追踪目标" : filter == 0 ? "当前目标已完成" : "没有符合筛选的目标";
        EmptyState.Hint = operators.Count + equipment.Count == 0 ? "在干员、装备页点 ☆，或在阵容、规划页点“追踪”。"
            : "可切换筛选查看其他目标，或到干员状态更新获取与精锐。";
        EquipmentSection.Visibility = equipmentShown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Terminal level card
        var level = _vm?.TerminalLevel ?? 0;
        LevelTitle.Text = level > 0 ? $"终端等级 {level}" : "终端等级未识别";
        var source = _vm is null || level == 0 ? "打开商店后按升级卡自动识别"
            : _vm.ManualTerminalLevel is not null ? "手动指定 · 自动识别追上后恢复自动"
            : _vm.IsTerminalLevelRestored ? "上次记录 · 打开商店后确认"
            : "自动识别";
        var pastStages = operators.Where(e => e.IsExpired).SelectMany(e => e.Stages.Split(['、', '→', '·', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct().ToList();
        LevelDetail.Text = pastStages.Count > 0 && level > 0 ? $"{source} · {string.Join("、", pastStages)}已过" : source;
        LevelDetail.ToolTip = _vm?.TerminalLevelText;
        PastHint.Text = level > 0 ? $"终端 {level} 级起不再标出" : "不再标出";
    }

    private static void Fill(FrameworkElement section, ItemsControl list, TextBlock count, IReadOnlyList<TrackedCell> cells)
    {
        list.ItemsSource = cells;
        count.Text = $"{cells.Count} 名";
        section.Visibility = cells.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCellRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackedCell cell) RemoveTrackingRequested?.Invoke(cell.Name);
    }

    /// <summary>Removes the planned transformation(s): 变形同构体 + job-change gear, or a v1.5 covenant without gear.</summary>
    private void OnCellClearTransform(object sender, RoutedEventArgs e)
    {
        if (_vm is null || (sender as FrameworkElement)?.DataContext is not TrackedCell cell) return;
        foreach (var t in cell.Transformations) _vm.ClearPlannedTransformation(cell.Name, t.EquipmentName, EquipmentRules);
        if (cell.LegacyCovenant.Length > 0) _vm.ClearPlannedTransformation(cell.Name, "", EquipmentRules);
    }

    /// <summary>购买清单 / 干员状态 / 地图模拟。</summary>
    private void OnTrackingPageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeployPage is null || ShoppingPage is null) return;
        bool deploy = TrackingPageList.SelectedIndex > 0;
        DeployPage.Visibility = deploy ? Visibility.Visible : Visibility.Collapsed;
        ShoppingPage.Visibility = deploy ? Visibility.Collapsed : Visibility.Visible;
        if (deploy) DeployPage.ShowPage(showPool: TrackingPageList.SelectedIndex == 1);
        if (IsLoaded) ASPAssistant.App.Windows.PageTransition.SlideIn(deploy ? DeployPage : ShoppingPage, fromX: deploy ? 14 : -14);
    }

    public void SetEquipment(IReadOnlyList<Equipment> equipment)
        => SetValue(EquipmentDatabaseProperty, equipment.ToDictionary(e => e.Name));

    private void OnTrackedEquipmentToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is EquipmentCell { DataContext: Equipment equipment })
            RemoveTrackingRequested?.Invoke(equipment.Name);
    }

    private void OnTrackedEquipmentAcquired(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is EquipmentCell { DataContext: Equipment equipment })
            ToggleAcquiredRequested?.Invoke(equipment.Name);
    }

    private void OnTogglePast(object sender, RoutedEventArgs e)
    {
        var open = PastBody.Visibility != Visibility.Visible;
        PastBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PastToggle.Content = (open ? "▾ " : "▸ ") + "已过期的过渡干员";
    }

    private void OnToggleGroup(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var body = (FrameworkElement)button.Tag;
        var open = body.Visibility != Visibility.Visible;
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        button.Content = open ? "收起" : "展开";
    }

    private void OnCompletionFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is not null) RefreshSummary();
    }

    private void OnCellOpenStatus(object sender, RoutedEventArgs e)
    {
        var data = MenuContext(sender);
        if (data is not TrackedCell cell) return;
        TrackingPageList.SelectedIndex = 1;
        DeployPage.FocusOperator(cell.Name);
    }

    /// <summary>Acquisition for equipment names missing from the data.</summary>
    private void OnToggleAcquired(object sender, RoutedEventArgs e)
    {
        if (MenuContext(sender) is TrackingEntry entry) ToggleAcquiredRequested?.Invoke(entry.Name);
    }

    /// <summary>A menu item's data, falling back to the element the menu belongs to.</summary>
    private static object? MenuContext(object sender) => sender is FrameworkElement { DataContext: { } data } ? data
        : (sender as FrameworkElement)?.Parent is ContextMenu { PlacementTarget: FrameworkElement target } ? target.DataContext : null;

    private void OnRemoveTracking(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackingEntry entry) RemoveTrackingRequested?.Invoke(entry.Name);
    }

    private void OnClearAllTracking(object sender, RoutedEventArgs e)
    {
        ClearAllTrackingRequested?.Invoke();
    }
}

public sealed record TrackedCovenantChip(string Name, int Count, bool IsCore);

public enum TrackedCellKind { Final, Transition, Past }

/// <summary>One tracked operator on the tracking page: grouped by role, ringed in the shop marker color.</summary>
public sealed record TrackedCell(string Name, TrackedCellKind Kind, string Sub, Operator? Operator,
    IReadOnlyList<EquipmentTransformation> Transformations, string LegacyCovenant)
{
    /// <summary>Marked 已获取: kept on the list, no longer starred in the shop.</summary>
    public bool IsAcquired { get; init; }
    public string Progress { get; init; } = "";
    public string Initial => Name.Length > 0 ? Name[..1] : "";
    public string IconPath => Operator?.IconPath ?? "";
    public bool HasDetails => Operator is not null;
    public bool HasTransform => Transformations.Count > 0 || LegacyCovenant.Length > 0;
    /// <summary>Transformation target(s), e.g. “不屈” or “不屈、精准” (暴躁铁皮 can hold several).</summary>
    public string TransformedCovenant => string.Join("、",
        Transformations.Select(t => t.Covenant).Append(LegacyCovenant).Where(c => c.Length > 0).Distinct());

    public static TrackedCell From(TrackingEntry entry, Operator? op, LineupEquipmentService? rules)
    {
        var kind = entry.IsExpired ? TrackedCellKind.Past
            : entry.IsTransition ? TrackedCellKind.Transition : TrackedCellKind.Final;
        IReadOnlyList<EquipmentTransformation> transformations = rules?.Transformations(new LineupSlot
        {
            OperatorName = entry.Name, Equipments = entry.Equipments, IsElite = entry.IsElite
        }) ?? [];
        var cell = new TrackedCell(entry.Name, kind, "", op, transformations, entry.TransformedCovenant ?? "");
        string sub;
        if (kind != TrackedCellKind.Final && !string.IsNullOrEmpty(entry.Stages)) sub = entry.Stages;
        else
        {
            var covenants = op is null ? new List<string>() : [.. op.CoreCovenants, .. op.AdditionalCovenants];
            var text = string.Join(" · ", covenants.Distinct());
            sub = cell.HasTransform ? $"转职 {cell.TransformedCovenant}" + (text.Length > 0 ? $" · {text}" : "")
                : text.Length > 0 ? text : op is null ? "无数据" : "";
        }
        if (entry.IsAcquired) sub = sub.Length > 0 ? $"已完成 · {sub}" : "已完成";
        return cell with { Sub = sub, IsAcquired = entry.IsAcquired };
    }
}
