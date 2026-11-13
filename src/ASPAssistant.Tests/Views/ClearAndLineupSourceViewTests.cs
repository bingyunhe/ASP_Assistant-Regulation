// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class ClearAndLineupSourceViewTests
{
    public static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var folder = Path.Combine(Path.GetTempPath(), "clear-lineup-ui-" + Guid.NewGuid().ToString("N"));
        Operator[] operators = [new() { Name = "甲", Tier = 1 }, new() { Name = "乙", Tier = 1 }, new() { Name = "丙", Tier = 1 }, new() { Name = "丁", Tier = 1 }];
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var settings = new SettingsManager(folder);
        var store = new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules);
        var lineups = new LineupViewModel(store, calculator);
        var tracking = new TrackingViewModel { TrackMultipleLineups = true, DetectedTerminalLevel = 6, IsTerminalLevelRestored = true };
        var a = new Lineup { Id = "a", Name = "被追踪的阵容", Slots = [new() { OperatorName = "甲", IsElite = true }] };
        var b = new Lineup { Id = "b", Name = "未追踪的阵容", Slots = [new() { OperatorName = "丙" }] };
        lineups.LoadLineups([a, b]);
        var operatorVm = new OperatorBrowseViewModel();
        operatorVm.LoadOperators(operators.ToList());
        var bans = new BanViewModel();
        operatorVm.SetBanChecker(bans.IsBanned);
        var panel = new SidePanelWindow(operatorVm, new EquipmentBrowseViewModel(), tracking,
            new GameStateViewModel(), bans, new EnemyViewModel(), lineups,
            new LineupShareService(calculator.EquipmentRules, Path.Combine(folder, "share")), operators, [], [], settings,
            GameShopTheme.Terminal, new SidePanelPositionSettings { FreeMovement = true })
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = 310, Height = 900 };
        try
        {
            panel.Show();
            Pump();
            panel.PlannerVm.CurrentTerminalLevel.Should().Be(6, "a restored terminal level is available before the next OCR change");
            tracking.IsTerminalLevelRestored = false;
            tracking.DetectedTerminalLevel = 6;
            panel.PlannerVm.CurrentTerminalLevel.Should().Be(6);
            tracking.ResetTerminalLevel();
            tracking.DetectedTerminalLevel = 4;
            panel.PlannerVm.CurrentTerminalLevel.Should().Be(4);
            tracking.ResetTerminalLevel();

            var operatorView = (OperatorBrowseView)panel.FindName("OperatorView");
            bans.SetBans(["甲"]);
            bans.SetCovenantBans(["炎"]);
            bans.ToggleBan("乙");
            operatorVm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
            operatorVm.SetAvailableCovenants(["维多利亚"]);
            ((MenuItem)operatorView.FindName("ClearHighlightsButton")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            bans.BannedNames.Should().BeEquivalentTo("乙");
            bans.BannedCovenants.Should().BeEmpty();
            operatorVm.HasRunInfo.Should().BeFalse();
            panel.PlannerVm.HasRunInfo.Should().BeFalse();
            operatorVm.FilteredOperators.Should().Contain(o => o.Name == "甲").And.NotContain(o => o.Name == "乙");
            bans.SetCovenantBans(["炎"]);
            ((MenuItem)operatorView.FindName("ClearBansButton")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            bans.BannedNames.Should().BeEmpty();
            bans.BannedCovenants.Should().BeEmpty();
            operatorVm.FilteredOperators.Should().HaveCount(4);
            lineups.SelectedAvailabilityFilter = AvailabilityFilter.Available;
            lineups.VisibleLineups.Should().HaveCount(2);
            operatorVm.SelectedAvailabilityFilter = AvailabilityFilter.All;
            var inputModel = new InputModel { Text = "测试输入" };
            var input = new TextBox();
            input.SetBinding(TextBox.TextProperty, new Binding(nameof(InputModel.Text)) { Source = inputModel, Mode = BindingMode.TwoWay });
            var inputHost = new Window { Content = input, Width = 280, Height = 50, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            inputHost.Show();
            try
            {
                input.Style = (Style)panel.FindResource("DialogTextBox");
                foreach (var theme in Enum.GetValues<GameShopTheme>())
                foreach (var night in new[] { false, true })
                {
                    ThemeManager.Apply(theme, night);
                    inputModel.Text = "测试输入";
                    Pump();
                    var button = WaitForClearButtonVisibility(input, Visibility.Visible);
                    button.Visibility.Should().Be(Visibility.Visible);
                    button.CommandTarget.Should().BeSameAs(input);
                    button.IsEnabled.Should().BeTrue();
                    button.Content.Should().BeNull();
                    button.Tag.Should().NotBeNull();
                    ClickClear(input);
                    input.Text.Should().BeEmpty();
                    inputModel.Text.Should().BeEmpty("clearing updates even a LostFocus binding immediately");
                    BindingOperations.IsDataBound(input, TextBox.TextProperty).Should().BeTrue();
                    button = WaitForClearButtonVisibility(input, Visibility.Collapsed);
                    button.Visibility.Should().Be(Visibility.Collapsed);
                    inputModel.Text = "再次输入";
                    Pump();
                    input.Text.Should().Be("再次输入");
                    Save(input, $"{theme}-{night}-text-clear");
                    input.IsReadOnly = true;
                    button = WaitForClearButtonVisibility(input, Visibility.Collapsed);
                    button.Visibility.Should().Be(Visibility.Collapsed);
                    input.IsReadOnly = false;
                }
            }
            finally { inputHost.Close(); }

            var mainTabs = (TabControl)panel.FindName("MainTabs");
            mainTabs.SelectedIndex = 3;
            Pump();
            var lineupView = (LineupListView)panel.FindName("LineupView");
            tracking.TrackLineup(a);
            Pump();
            lineupView.TrackedLineupIds.Should().BeEquivalentTo("a");
            var card = Descendants<Border>(lineupView).Single(x => Equals(x.Tag, "a"));
            card.Background.Should().Be(panel.FindResource("AccentSoftBrush"));
            Descendants<TextBlock>(card).Should().Contain(x => x.Text == "追踪中" && x.Visibility == Visibility.Visible);
            ((Button)lineupView.FindName("ClearHighlightsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            lineupView.TrackedLineupIds.Should().BeEmpty();
            tracking.IsLineupTracked("a").Should().BeTrue();
            tracking.TrackedOperators.Should().ContainSingle().Which.Name.Should().Be("甲");
            tracking.TrackLineup(a);
            tracking.TrackLineup(b);
            Pump();
            lineupView.TrackedLineupIds.Should().BeEquivalentTo("a", "b");

            var state = new DeployPoolState();
            var trackingView = (TrackingView)panel.FindName("TrackingView");
            trackingView.DeployPage.Initialize(operators.ToDictionary(o => o.Name), [], null, tracking, settings, state, false);
            mainTabs.SelectedIndex = 2;
            var pages = (ListBox)trackingView.FindName("TrackingPageList");
            pages.SelectedIndex = 0;
            Pump();
            var pending = Descendants<Button>((ItemsControl)trackingView.FindName("FinalCells"))
                .Single(x => x.DataContext is TrackedCell c && c.Name == "甲" && Equals(x.Content, "待获取"));
            pending.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var search = (TextBox)trackingView.DeployPage.FindName("PoolSearch");
            search.Text.Should().Be("甲");
            var poolCard = Descendants<Grid>((StackPanel)trackingView.DeployPage.FindName("PoolGroups"))
                .Single(x => x.ToolTip is string tip && tip.StartsWith("甲 · "));
            poolCard.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Pump();
            search.Text.Should().BeEmpty("acquiring a navigated target restores the full list");
            state.IsOwned("甲").Should().BeTrue();
            tracking.IsTracked("甲").Should().BeTrue("an elite target remains pending after ordinary acquisition");
            trackingView.DeployPage.FocusOperator("丙");
            search.Text = "手动条件";
            ClickClear(search);
            Pump();
            search.Text.Should().BeEmpty();
            search.Text = "丙"; // A manually entered filter is not a navigated lookup.
            Pump();
            poolCard = Descendants<Grid>((StackPanel)trackingView.DeployPage.FindName("PoolGroups"))
                .Single(x => x.ToolTip is string tip && tip.StartsWith("丙 · "));
            poolCard.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Pump();
            search.Text.Should().Be("丙");
            ClickClear(search);

            search.Text = "不匹配新增干员的条件";
            var add = Descendants<Button>(trackingView.DeployPage).Single(x => Equals(x.Content, "＋ 添加"));
            var pickerSeen = false;
            var pickerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            pickerTimer.Tick += (_, _) =>
            {
                var picker = Application.Current.Windows.OfType<OperatorPickerWindow>().FirstOrDefault();
                if (picker is null) return;
                pickerTimer.Stop();
                pickerSeen = true;
                var list = (ListBox)picker.FindName("OperatorsList");
                list.SelectedItem = operators.Single(o => o.Name == "丁");
                list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(picker)!, 0, Key.Enter)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                picker.Result.Should().Be("丁");
            };
            pickerTimer.Start();
            try { add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { pickerTimer.Stop(); }
            pickerSeen.Should().BeTrue();
            Pump();
            search.Text.Should().BeEmpty();
            state.IsOwned("丁").Should().BeTrue();
            state.Extra.Should().Contain("丁");
            Descendants<TextBlock>((StackPanel)trackingView.DeployPage.FindName("PoolGroups")).Should().Contain(t => t.Text == "丁");
            var candidates = (IEnumerable<PlacementCandidate>)typeof(DeployView).GetMethod("Candidates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(trackingView.DeployPage, null)!;
            candidates.Should().Contain(c => c.Name == "丁", "the map planner receives the newly added owned operator");
            tracking.TrackedOperators.Should().NotContain(e => e.Name == "丁", "adding to the owned pool does not add a purchase target");
            trackingView.DeployPage.FindName("PoolButton").Should().BeNull("the reserve area no longer has a manage button");

            mainTabs.SelectedIndex = 0;
            Pump();
            var operatorSearch = Descendants<TextBox>(panel).Single(x => x.Name == "SearchBox" && x.DataContext == operatorVm);
            operatorSearch.Text = "甲";
            Pump();
            operatorVm.SearchText.Should().Be("甲");
            ClickClear(operatorSearch);
            operatorVm.SearchText.Should().BeEmpty();

            mainTabs.SelectedIndex = 3;
            Pump();
            var apply = new Lineup { Id = "a", Name = "已保存的新阵容", Slots = [new() { OperatorName = "乙" }] };
            ChooseConfirmation(panel, () => panel.SaveEditedLineupAsync(apply, true), true);
            tracking.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo("乙", "丙");
            var savedOnly = new Lineup { Id = "a", Name = "仅保存版本", Slots = [new() { OperatorName = "甲" }] };
            ChooseConfirmation(panel, () => panel.SaveEditedLineupAsync(savedOnly, true), false);
            tracking.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo("乙", "丙");
            (Await(Task.Run(store.LoadAllAsync))).Single(l => l.Id == "a").Name.Should().Be("仅保存版本");
            Await(panel.SaveEditedLineupAsync(new Lineup { Id = "untracked", Name = "未关联", Slots = [] }, true));
            using (var locked = new FileStream(Path.Combine(folder, "lineups", "a.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                Await(panel.SaveEditedLineupAsync(new Lineup { Id = "a", Name = "无法写入的版本", Slots = [new() { OperatorName = "甲" }] }, true));
            Application.Current.Windows.OfType<ConfirmDialog>().Should().BeEmpty();
            tracking.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo("乙", "丙");
            state.SetCount("乙", 2);
            state.SetElite("乙", true);
            tracking.SyncDeployPool();
            Await(trackingView.DeployPage.StartNewRunAsync());
            Pump();
            state.Owned.Should().BeEmpty();
            state.Elite.Should().BeEmpty();
            state.Counts.Should().BeEmpty();
            tracking.TrackedOperators.Should().OnlyContain(e => !e.IsAcquired);
            panel.PlannerVm.CurrentTerminalLevel.Should().Be(0);
            trackingView.DeployPage.CurrentPlan.Should().BeNull();
            ((IEnumerable<PlacementCandidate>)typeof(DeployView).GetMethod("Candidates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(trackingView.DeployPage, null)!).Should().BeEmpty();
            Await(settings.LoadDeployPoolAsync()).Owned.Should().BeEmpty();
            foreach (var theme in Enum.GetValues<GameShopTheme>())
            foreach (var night in new[] { false, true })
            foreach (var width in new[] { 280, 400 })
            {
                ThemeManager.Apply(theme, night);
                panel.Width = width;
                Await(Task.Delay(240));
                Pump();
                foreach (var button in Descendants<Button>(lineupView).Where(x => x.IsVisible))
                {
                    var bounds = button.TransformToAncestor(lineupView).TransformBounds(new Rect(button.RenderSize));
                    bounds.Right.Should().BeLessThanOrEqualTo(lineupView.ActualWidth + 0.1);
                }
                lineupView.TrackedLineupIds.Should().BeEquivalentTo("a", "b");
                foreach (var id in new[] { "a", "b" })
                {
                    var trackedCard = Descendants<Border>(lineupView).Single(x => Equals(x.Tag, id));
                    Descendants<TextBlock>(trackedCard).Should().Contain(x => x.Text == "追踪中" && x.Visibility == Visibility.Visible,
                        $"tracked badges stay visible after saving and theme switches ({theme}, {night})");
                }
                Save(lineupView, $"{theme}-{night}-{width}-lineups");
            }
        }
        finally
        {
            User32.UnregisterHotKey(new System.Windows.Interop.WindowInteropHelper(panel).Handle, 0x4153);
            panel.Close();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            ThemeManager.Apply(GameShopTheme.Terminal, true);
        }
    }

    private static void ChooseConfirmation(SidePanelWindow panel, Func<Task> action, bool apply)
    {
        bool seen = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<ConfirmDialog>().FirstOrDefault();
            if (dialog is null) return;
            timer.Stop();
            seen = true;
            ((Button)dialog.FindName("OkButton")).Content.Should().Be("应用到追踪栏");
            ((Button)dialog.FindName("CancelButton")).Content.Should().Be("仅保存");
            dialog.DialogResult = apply;
        };
        timer.Start();
        try { Await(action()); seen.Should().BeTrue(); }
        finally { timer.Stop(); }
    }

    private static Button ClearButton(TextBox input)
    {
        input.ApplyTemplate();
        return (Button)input.Template.FindName("ClearTextButton", input);
    }
    private static Button WaitForClearButtonVisibility(TextBox input, Visibility expected)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            input.UpdateLayout();
            Pump();
            var button = ClearButton(input);
            if (button.Visibility == expected) return button;
            Thread.Sleep(1);
        }
        throw new TimeoutException($"Current clear-button template did not become {expected}; text length={input.Text.Length}, read-only={input.IsReadOnly}.");
    }

    private static void ClickClear(TextBox input)
    {
        var command = (RoutedCommand)ClearButton(input).Command;
        command.CanExecute(null, input).Should().BeTrue();
        command.Execute(null, input);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Await(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until) Pump();
        task.IsCompleted.Should().BeTrue();
        task.GetAwaiter().GetResult();
    }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Save(FrameworkElement view, string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestResults", "clear-source-previews");
        Directory.CreateDirectory(path);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(path, name + ".png"));
        encoder.Save(output);
    }
    private sealed class InputModel : INotifyPropertyChanged
    {
        private string _text = "";
        public string Text { get => _text; set { _text = value; PropertyChanged?.Invoke(this, new(nameof(Text))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
