// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASPAssistant.App;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class TrackingWorkflowViewTests
{
    public static void VerifyEquipmentAndGroups()
    {
        const string gear = "名称很长的测试装备甲";
        Operator[] operators = [new() { Name = "大成目标" }, new() { Name = "过渡目标" }];
        Equipment[] equipment = [new() { Name = gear }, new() { Name = "变形同构体" }];
        var vm = new TrackingViewModel();
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "大成目标", Equipments = [gear, "变形同构体", "旧版已移除装备"] }],
            EarlySlots = [new() { OperatorName = "过渡目标" }] });
        var view = new TrackingView { DataContext = vm };
        view.SetOperators(operators);
        view.SetEquipment(equipment);
        view.SetCovenantCalculator(new CovenantCalculator(operators, [], new Dictionary<string, string>()));
        var acquiredEvents = 0;
        view.ToggleAcquiredRequested += name => { acquiredEvents++; vm.ToggleAcquired(name); };
        view.RemoveTrackingRequested += vm.RemoveTracking;
        var host = new Window { Content = view, Width = 280, Height = 1000, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0, Left = -20000, Top = -20000 };
        host.Show();
        try
        {
            var finals = (ItemsControl)view.FindName("FinalCells");
            var transitions = (ItemsControl)view.FindName("TransitionCells");
            var gears = (ItemsControl)view.FindName("EquipmentCells");
            var finalToggle = (Button)view.FindName("FinalToggle");
            var transitionToggle = (Button)view.FindName("TransitionToggle");
            var filters = (ListBox)view.FindName("CompletionFilterList");
            Layout(view, 280);
            finalToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            finals.Visibility.Should().Be(Visibility.Collapsed);
            transitions.Visibility.Should().Be(Visibility.Visible);
            ((TextBlock)view.FindName("FinalCount")).Text.Should().Be("1 名");
            vm.IsTracked("大成目标").Should().BeTrue();
            transitionToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            transitions.Visibility.Should().Be(Visibility.Collapsed);
            var gearCell = Descendants<EquipmentCell>(gears).Single(c => ((Equipment)c.DataContext).Name == gear);
            ((Button)gearCell.FindName("AcquireButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(view, 280);
            acquiredEvents.Should().Be(1);
            vm.IsTracked(gear).Should().BeFalse();
            vm.TrackedOperators.Single(e => e.Name == "大成目标").Equipments.Should().Contain(gear);
            finals.Visibility.Should().Be(Visibility.Collapsed, "state refresh must retain the user's collapse choice");
            filters.SelectedIndex = 2;
            Layout(view, 280);
            gearCell = Descendants<EquipmentCell>(gears).Single(c => ((Equipment)c.DataContext).Name == gear);
            var completed = (Button)gearCell.FindName("AcquireButton");
            completed.Content.Should().Be("已获取");
            completed.IsEnabled.Should().BeFalse();
            ((Button)gearCell.FindName("TrackButton")).IsEnabled.Should().BeTrue("acquired equipment can still be removed");

            var browseVm = new EquipmentBrowseViewModel();
            browseVm.LoadEquipment(equipment.ToList(), [], [], new Dictionary<string, string>());
            var browse = new EquipmentBrowseView { DataContext = browseVm, IsTrackedCheck = vm.IsTracked };
            browse.TrackingRequested += vm.AddTracking;
            browse.UntrackingRequested += vm.RemoveTracking;
            host.Content = browse;
            Layout(browse, 280);
            var browseCell = Descendants<EquipmentCell>(browse).Single(c => ((Equipment)c.DataContext).Name == gear);
            ((Button)browseCell.FindName("AcquireButton")).Visibility.Should().Be(Visibility.Collapsed);
            ((Button)browseCell.FindName("TrackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            vm.IsTracked(gear).Should().BeTrue("the equipment tab can add the acquired target again");
            host.Content = view;
            Layout(view, 280);
            gearCell = Descendants<EquipmentCell>(gears).Single(c => ((Equipment)c.DataContext).Name == gear);
            ((Button)gearCell.FindName("AcquireButton")).IsEnabled.Should().BeTrue();
            var legacy = Descendants<ContentControl>(gears).Single(c => c.Tag is TrackingEntry e && e.Name == "旧版已移除装备");
            Descendants<Button>(legacy).Single(b => Equals(b.ToolTip, "取消追踪")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            vm.IsTracked("旧版已移除装备").Should().BeFalse();
            acquiredEvents.Should().Be(1, "deleting a target must not mark it acquired");
            vm.AddTracking("旧版已移除装备", TrackingType.Equipment);
            finalToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            finals.Visibility.Should().Be(Visibility.Visible);
            transitions.Visibility.Should().Be(Visibility.Collapsed);
            transitionToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            foreach (var theme in Enum.GetValues<GameShopTheme>())
            foreach (var night in new[] { false, true })
            foreach (var width in new[] { 280, 400 })
            {
                ThemeManager.Apply(theme, night);
                Layout(view, width);
                var rows = Descendants<ContentControl>(gears).Where(c => c.Tag is TrackingEntry).ToList();
                rows.Should().HaveCount(3);
                var bounds = rows.Select(c => c.TransformToAncestor(gears).TransformBounds(new Rect(c.RenderSize))).ToList();
                for (var i = 0; i < bounds.Count; i++)
                {
                    bounds[i].Width.Should().BeGreaterThan(width - 60, "each row uses the full width");
                    if (i > 0) bounds[i].Top.Should().BeGreaterThanOrEqualTo(bounds[i - 1].Bottom, "only one equipment appears per row");
                }
                Descendants<FrameworkElement>(gears).Should().OnlyContain(e => e.ContextMenu == null);
                Descendants<Button>(gears).Should().NotContain(b => Equals(b.Content, "继续追踪"));
                foreach (var button in Descendants<Button>(view).Where(b => b.IsVisible))
                    button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize)).Right.Should().BeLessThanOrEqualTo(width + .1);
                Save(view, $"equipment-groups-{theme}-{night}-{width}");
            }
        }
        finally { host.Close(); ThemeManager.Apply(GameShopTheme.Terminal, true); }
    }

    public static void Verify()
    {
        var operators = new[] { "荒芜拉普兰德", "魔王", "冻河", "棘刺", "流明", "温蒂", "锏", "赫拉格", "乌啼" }
            .Select(n => new Operator { Name = n, Tier = 6, CoreCovenants = ["乌萨斯"], AdditionalCovenants = ["术士"] }).ToList();
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var vm = new TrackingViewModel();
        vm.TrackLineup(new Lineup { Slots = operators.Select(o => new LineupSlot { OperatorName = o.Name }).ToList() });
        var state = new DeployPoolState();
        foreach (var op in operators.Skip(1)) state.SetOwned(op.Name, true);
        var view = new TrackingView { DataContext = vm };
        view.SetOperators(operators);
        view.SetCovenantCalculator(calculator);
        view.DeployPage.Initialize(operators.ToDictionary(o => o.Name), [], null, vm,
            new SettingsManager(Path.Combine(Path.GetTempPath(), "unused-tracking-layout")), state, false);
        ((ListBox)view.FindName("TrackingPageList")).SelectedIndex = 1;
        Layout(view, 400);
        var partial = view.DeployPage.ActualWidth;
        state.SetOwned(operators[0].Name, true);
        vm.SyncDeployPool();
        Layout(view, 400);
        var all = view.DeployPage.ActualWidth;
        var folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "tracking-workflow");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "width-probe.json"), System.Text.Json.JsonSerializer.Serialize(new { partial, all, viewport = view.ActualWidth }));
        partial.Should().BeApproximately(400, 0.1, "the status page must fill the available width even before every operator is owned");
        all.Should().BeApproximately(partial, 0.1);
        foreach (var width in new[] { 280, 400 })
        {
            Layout(view, width);
            var name = Descendants<TextBlock>(view.DeployPage).Single(t => t.Text == "荒芜拉普兰德");
            name.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);
            name.ActualWidth.Should().BeGreaterThan(0);
            var line = (DockPanel)VisualTreeHelper.GetParent(name);
            var elite = line.Children.OfType<Border>().Should().ContainSingle().Which;
            var bounds = elite.TransformToAncestor(line).TransformBounds(new Rect(elite.RenderSize));
            bounds.Right.Should().BeLessThanOrEqualTo(line.ActualWidth + 0.1, "long names must leave the elite button clickable");
        }
    }

    public static void VerifyWorkflow()
    {
        var data = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
        var operators = Task.Run(data.LoadOperatorsAsync).GetAwaiter().GetResult();
        var (covenants, mappings) = Task.Run(data.LoadCovenantsAsync).GetAwaiter().GetResult();
        var db = operators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First());
        var calculator = new CovenantCalculator(operators, covenants, mappings);
        var vm = new TrackingViewModel();
        var state = new DeployPoolState { Owned = ["塔露拉"] };
        vm.SetDeployPool(state);
        vm.TrackLineup(new Lineup
        {
            Slots = [new() { OperatorName = "塔露拉", IsElite = true }, new() { OperatorName = "魔王" }],
            EarlySlots = [new() { OperatorName = "冻河" }]
        });
        vm.TrackedOperators.Add(new TrackingEntry { Name = "角峰", Role = TrackingRole.Transition, Stages = "开局", ExpiresAt = 3 });
        vm.AddTracking("测试装备", TrackingType.Equipment);
        vm.DetectedTerminalLevel = 4;
        var view = new TrackingView { DataContext = vm };
        view.SetOperators(operators);
        view.SetEquipment([]);
        view.SetCovenantCalculator(calculator);
        view.ToggleAcquiredRequested += vm.ToggleAcquired;
        view.DeployPage.Initialize(db, covenants, null, vm,
            new SettingsManager(Path.Combine(Path.GetTempPath(), "unused-tracking-workflow")), state, false);
        var host = new Window { Content = view, Width = 280, Height = 1000, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0, Left = -20000, Top = -20000 };
        host.Show();
        try
        {
        var pages = (ListBox)view.FindName("TrackingPageList");
        var filters = (ListBox)view.FindName("CompletionFilterList");
        var finals = (ItemsControl)view.FindName("FinalCells");
        var transition = (ItemsControl)view.FindName("TransitionCells");
        var equipments = (ItemsControl)view.FindName("EquipmentCells");
        Layout(view, 280);
        finals.Items.Cast<TrackedCell>().Single(c => c.Name == "塔露拉").Progress.Should().Be("待精锐");
        finals.Items.Count.Should().Be(2);
        transition.Items.Count.Should().Be(1);
        ((StackPanel)view.FindName("PastBody")).Visibility.Should().Be(Visibility.Collapsed);
        var before = ((ItemsControl)view.FindName("TrackedCovenantChips")).Items.Cast<TrackedCovenantChip>().ToArray();
        state.SetElite("塔露拉", true);
        vm.SyncDeployPool();
        Layout(view, 280);
        finals.Items.Cast<object>().Should().ContainSingle().Which.Should().BeOfType<TrackedCell>().Which.Name.Should().Be("魔王");
        ((ItemsControl)view.FindName("TrackedCovenantChips")).Items.Cast<TrackedCovenantChip>().Should().Equal(before);
        filters.SelectedIndex = 1;
        Layout(view, 280);
        finals.Items.Cast<object>().Should().ContainSingle().Which.Should().BeOfType<TrackedCell>().Which.Progress.Should().Be("已完成");
        var openStatus = Descendants<Button>(finals).Single(b => Equals(b.Content, "已完成"));
        openStatus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(view, 280);
        pages.SelectedIndex.Should().Be(1);
        ((TextBox)view.DeployPage.FindName("PoolSearch")).Text.Should().Be("塔露拉");
        ((Border)view.DeployPage.FindName("MapFooter")).Visibility.Should().Be(Visibility.Collapsed);
        ((TextBox)view.DeployPage.FindName("PoolSearch")).Clear();
        pages.SelectedIndex = 0;
        filters.SelectedIndex = 0;
        Layout(view, 280);
        var acquire = Descendants<Button>(equipments).Single(b => Equals(b.Content, "获取"));
        acquire.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(view, 280);
        vm.TrackedEquipment.Single().IsAcquired.Should().BeTrue();
        equipments.Items.Cast<object>().Should().BeEmpty();
        filters.SelectedIndex = 2;
        Layout(view, 280);
        equipments.Items.Cast<object>().Should().ContainSingle();
        foreach (var theme in Enum.GetValues<GameShopTheme>())
        foreach (var night in new[] { false, true })
        foreach (var width in new[] { 280, 310, 400 })
        {
            ThemeManager.Apply(theme, night);
            pages.SelectedIndex = 0;
            filters.SelectedIndex = 2;
            Layout(view, width);
            foreach (var button in Descendants<Button>(finals))
            {
                var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
                bounds.Right.Should().BeLessThanOrEqualTo(width + 0.1);
            }
            Save(view, $"{theme}-{night}-{width}-shopping");
            pages.SelectedIndex = 1;
            Layout(view, width);
            view.DeployPage.ActualWidth.Should().BeApproximately(width, 0.1);
            var pool = (StackPanel)view.DeployPage.FindName("PoolGroups");
            var extension = Descendants<Button>(pool).Single(b => b.Content is string text && text.Contains("扩展池"));
            extension.Content!.ToString().Should().StartWith("▸");
            extension.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(view, width);
            extension = Descendants<Button>(pool).Single(b => b.Content is string text && text.Contains("扩展池"));
            extension.Content!.ToString().Should().StartWith("▾");
            Save(view, $"{theme}-{night}-{width}-status-partial");
            state.SetOwned("魔王", true);
            state.SetOwned("冻河", true);
            vm.SyncDeployPool();
            Layout(view, width);
            view.DeployPage.ActualWidth.Should().BeApproximately(width, 0.1);
            Save(view, $"{theme}-{night}-{width}-status-owned");
            extension = Descendants<Button>(pool).Single(b => b.Content is string text && text.Contains("扩展池"));
            extension.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            state.SetOwned("魔王", false);
            state.SetOwned("冻河", false);
            vm.SyncDeployPool();
            pages.SelectedIndex = 2;
            Layout(view, width);
            ((Border)view.DeployPage.FindName("MapFooter")).Visibility.Should().Be(Visibility.Visible);
            Save(view, $"{theme}-{night}-{width}-no-map");
        }
        vm.ClearAllTracking();
        state.IsOwned("塔露拉").Should().BeTrue();
        state.IsElite("塔露拉").Should().BeTrue();
        pages.SelectedIndex = 0;
        Layout(view, 280);
        ((ASPAssistant.App.Controls.EmptyState)view.FindName("EmptyState")).Title.Should().Be("还没有追踪目标");
        ThemeManager.Apply(GameShopTheme.Terminal, true);
        }
        finally { host.Close(); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }

    private static void Save(FrameworkElement view, string name)
    {
        var output = Path.Combine(AppContext.BaseDirectory, "TestResults", "tracking-workflow");
        Directory.CreateDirectory(output);
        var image = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }

    private static void Layout(FrameworkElement view, double width)
    {
        if (Window.GetWindow(view) is { } host) host.Width = width;
        var settle = Task.Delay(240);
        while (!settle.IsCompleted) Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.Measure(new Size(width, 1000));
        view.Arrange(new Rect(0, 0, width, 1000));
        view.UpdateLayout();
    }
}
