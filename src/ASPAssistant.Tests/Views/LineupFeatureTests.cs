using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class LineupFeatureTests
{
    public static void Verify()
    {
        Operator[] operators = [new() { Name = "荒芜拉普兰德", Tier = 6, CoreCovenants = ["叙拉古"] },
            new() { Name = "角峰", Tier = 1, CoreCovenants = ["谢拉格"] }];
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var store = new LineupStore(Path.Combine(Path.GetTempPath(), "unused-lineup-ui"), calculator.EquipmentRules);
        foreach (var theme in Enum.GetValues<GameShopTheme>())
        foreach (var night in new[] { false, true })
        {
            ThemeManager.Apply(theme, night);
            var source = new Lineup { Name = "后期可用阵容", Slots = [new() { OperatorName = "荒芜拉普兰德" }],
                EarlySlots = [new() { OperatorName = "角峰" }] };
            var banned = new Lineup { Name = "后期不可用阵容", Slots = [new() { OperatorName = "角峰" }] };
            var vm = new LineupViewModel(store, calculator);
            vm.LoadLineups([source, banned]);
            vm.SetBanChecker(name => name == "角峰");
            var view = new LineupListView { DataContext = vm };
            var host = new Window { Content = view, ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 360, Height = 760 };
            try
            {
                host.Show();
                Pump();
                var filter = (ListBox)view.FindName("AvailabilityFilterList");
                filter.SelectedIndex.Should().Be(0);
                filter.SelectedIndex = 1;
                Pump();
                vm.VisibleLineups.Should().Equal(source);
                filter.SelectedIndex = 2;
                Pump();
                vm.VisibleLineups.Should().Equal(banned);
                filter.SelectedIndex = 0;
                Pump();
                foreach (var width in new[] { 280, 360 })
                {
                    host.Width = width;
                    Pump();
                    var items = (ItemsControl)view.FindName("LineupItems");
                    items.Items.Count.Should().Be(2);
                    var moveUp = Descendants<Button>(items).Where(b => Equals(b.ToolTip, "上移阵容")).ToList();
                    moveUp[0].IsEnabled.Should().BeFalse();
                    moveUp[1].IsEnabled.Should().BeTrue();
                    var moveDown = Descendants<Button>(items).Where(b => Equals(b.ToolTip, "下移阵容")).ToList();
                    moveDown[0].IsEnabled.Should().BeTrue();
                    moveDown[1].IsEnabled.Should().BeFalse();
                    foreach (var button in Descendants<Button>(items))
                    {
                        var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
                        bounds.Right.Should().BeLessThanOrEqualTo(view.ActualWidth + 1, "card actions must fit the sidebar");
                    }
                    Save(view, $"{theme}-{night}-lineups-{width}");
                }
                (Lineup? Lineup, int Direction) requested = (null, 9);
                view.MoveLineupRequested += (lineup, direction) => requested = (lineup, direction);
                Descendants<Button>(view).Last(b => Equals(b.ToolTip, "置顶阵容"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                requested.Should().Be((banned, 0));
                vm.SetBanChecker(_ => false);
                filter.SelectedIndex = 2;
                Pump();
                ((EmptyState)view.FindName("EmptyHint")).Title.Should().Be("没有符合条件的阵容");
            }
            finally { host.Close(); }

            var editorVm = new LineupEditorViewModel(source, operators, [], calculator);
            var editor = new LineupEditorWindow(editorVm, []);
            try
            {
                var content = (FrameworkElement)editor.Content;
                content.Measure(new Size(1000, 740));
                content.Arrange(new Rect(0, 0, 1000, 740));
                content.UpdateLayout();
                var elite = Descendants<CheckBox>(content).Single(check => Equals(check.Content, "精锐"));
                elite.IsChecked = true;
                editorVm.Slots[0].IsElite.Should().BeTrue();
                source.Slots[0].IsElite.Should().BeFalse("the editor modifies a draft");
                editorVm.ToLineup().Slots[0].IsElite.Should().BeTrue();
                content.UpdateLayout();
                Save(content, $"{theme}-{night}-editor");
                Descendants<CheckBox>(content).Single(check => Equals(check.Content, "精锐")).IsChecked = false;
                editorVm.Slots[0].IsElite.Should().BeFalse();
            }
            finally { editor.Close(); }

            var candidates = Enumerable.Range(1, 14).Select(i => new Operator { Name = $"自选候选{i:D2}", IsCustom = true,
                Tier = 5, CoreCovenants = ["谢拉格"], IsSelectedCustom = i <= 12 }).ToList();
            var browseVm = new OperatorBrowseViewModel { SelectedOperatorSource = OperatorSourceFilter.Default };
            browseVm.LoadOperators([.. operators, .. candidates]);
            var browse = new OperatorBrowseView { DataContext = browseVm };
            var browseHost = new Window { Content = browse, ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 280, Height = 900 };
            try
            {
                browseHost.Show();
                Pump();
                browseVm.FilteredOperators.Should().HaveCount(2);
                var sourceTabs = (ListBox)browse.FindName("OperatorSourceList");
                sourceTabs.SelectedIndex.Should().Be(0);
                Save(browse, $"{theme}-{night}-default-operators-280");
                sourceTabs.SelectedIndex = 1;
                Pump();
                browseVm.FilteredOperators.Should().HaveCount(12).And.OnlyContain(op => op.IsCustom);
                ((Button)browse.FindName("ManageCustomOperatorsButton")).Visibility.Should().Be(Visibility.Visible);
                ((TextBlock)((Button)browse.FindName("ManageCustomOperatorsButton")).Content).Text.Should().Be("管理自选 · 12/12");
                Save(browse, $"{theme}-{night}-custom-operators-280");
                var picker = new OperatorPickerWindow([.. operators, .. candidates], "", new HashSet<string>());
                try
                {
                    var pickerContent = (FrameworkElement)picker.Content;
                    pickerContent.Measure(new Size(778, 698));
                    pickerContent.Arrange(new Rect(0, 0, 778, 698));
                    pickerContent.UpdateLayout();
                    ((ListBox)picker.FindName("OperatorsList")).Items.Count.Should().Be(14);
                }
                finally { picker.Close(); }
            }
            finally { browseHost.Close(); }
            var selection = new CustomOperatorSelectionWindow(candidates);
            try
            {
                var content = (FrameworkElement)selection.Content;
                content.Measure(new Size(618, 658));
                content.Arrange(new Rect(0, 0, 618, 658));
                content.UpdateLayout();
                var checks = Descendants<CheckBox>(content).ToList();
                checks.Count(check => check.IsChecked == true).Should().Be(12);
                checks.Where(check => check.IsChecked != true).Should().OnlyContain(check => !check.IsEnabled);
                checks[0].IsChecked = false;
                checks.Should().OnlyContain(check => check.IsEnabled);
                checks[12].IsChecked = true;
                ((TextBox)selection.FindName("SearchBox")).Text = "候选13";
                Save(content, $"{theme}-{night}-custom-selection");
                Descendants<Button>(content).Single(button => Equals(button.Content, "保存")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                selection.Result.Should().HaveCount(12).And.Contain("自选候选13").And.NotContain("自选候选01");
                candidates[0].IsSelectedCustom.Should().BeTrue("dialog edits do not mutate the saved selection");
            }
            finally { selection.Close(); }
        }
        ThemeManager.Apply(GameShopTheme.Terminal, true);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) yield return result;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Save(FrameworkElement content, string name)
    {
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "lineup-feature-previews");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
