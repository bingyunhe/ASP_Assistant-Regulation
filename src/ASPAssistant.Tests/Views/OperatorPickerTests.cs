using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class OperatorPickerTests
{
    public static void VerifySelection()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        try
        {
            var operators = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"))
                .LoadOperatorsAsync().GetAwaiter().GetResult();
            foreach (var (name, next) in new[]
            {
                ("褐果", "灰喉"), ("拉普兰德", "烈夏"), ("夏栎", "野鬃"),
                ("安哲拉", "奥斯塔"), ("复奏", "瑰盐")
            })
            {
                var picker = new OperatorPickerWindow(operators, "", new HashSet<string>())
                {
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, Opacity = 0
                };
                try
                {
                    var list = (ListBox)picker.FindName("OperatorsList");
                    list.Height = 261;
                    picker.Show();
                    var content = (FrameworkElement)picker.Content;
                    content.Measure(new Size(778, 698));
                    content.Arrange(new Rect(0, 0, 778, 698));
                    content.UpdateLayout();
                    var scroll = Descendants<ScrollViewer>(list).First();
                    var index = list.Items.Cast<Operator>().ToList().FindIndex(op => op.Name == name);
                    scroll.ScrollToVerticalOffset(index - 4);
                    Pump();
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
                    var point = row.TranslatePoint(new Point(100, 10), list);
                    var hit = VisualTreeHelper.HitTest(list, point).VisualHit;
                    ((Operator)((ListBoxItem)ItemsControl.ContainerFromElement(list, hit)).Content).Name.Should().Be(name);
                    RaiseMouse((UIElement)hit, Mouse.PreviewMouseDownEvent);

                    // A partially visible selected row scrolls before the mouse is released.
                    list.SelectedItem = row.Content;
                    row.BringIntoView();
                    Pump();
                    hit = VisualTreeHelper.HitTest(list, point).VisualHit;
                    ((Operator)((ListBoxItem)ItemsControl.ContainerFromElement(list, hit)).Content).Name.Should().Be(next,
                        "the same screen point now hits the following row after scrolling");
                    RaiseMouse((UIElement)hit, Mouse.MouseUpEvent);
                    picker.Result.Should().Be(name, "scrolling between press and release must preserve the clicked operator");
                }
                finally { picker.Close(); }
            }

            foreach (var strategySelected in new[] { false, true })
            foreach (var useKeyboard in new[] { false, true })
            {
                var picker = new OperatorPickerWindow(operators, "",
                    strategySelected ? new HashSet<string> { "band_mech" } : new HashSet<string>());
                try
                {
                    var filters = (OperatorBrowseViewModel)picker.DataContext;
                    filters.SearchText = "暴躁铁皮";
                    var list = (ListBox)picker.FindName("OperatorsList");
                    var content = (FrameworkElement)picker.Content;
                    content.Measure(new Size(778, 698));
                    content.Arrange(new Rect(0, 0, 778, 698));
                    content.UpdateLayout();
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    if (useKeyboard)
                        PressEnter(list, content);
                    else
                    {
                        RaiseMouse(row, Mouse.PreviewMouseDownEvent);
                        RaiseMouse(row, Mouse.MouseUpEvent);
                    }
                    picker.Result.Should().Be(strategySelected ? LineupEquipmentService.MechName : null);
                }
                finally { picker.Close(); }
            }

            foreach (var useKeyboard in new[] { false, true })
            {
                var picker = new OperatorPickerWindow(operators, "", new HashSet<string>());
                try
                {
                    var filters = (OperatorBrowseViewModel)picker.DataContext;
                    filters.SearchText = "拉普兰德";
                    filters.SelectedTierFilters = [1];
                    var list = (ListBox)picker.FindName("OperatorsList");
                    var content = (FrameworkElement)picker.Content;
                    content.Measure(new Size(778, 698));
                    content.Arrange(new Rect(0, 0, 778, 698));
                    content.UpdateLayout();
                    list.Items.Cast<Operator>().Select(op => op.Name).Should().Equal("拉普兰德");
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    if (useKeyboard)
                    {
                        PressEnter(list, content);
                    }
                    else
                    {
                        // Releasing over a row without a row press (e.g. after dragging the scrollbar) is ignored.
                        RaiseMouse(row, Mouse.MouseUpEvent);
                        picker.Result.Should().BeNull();
                        RaiseMouse(Descendants<ScrollBar>(list).First(bar => bar.Orientation == Orientation.Vertical), Mouse.PreviewMouseDownEvent);
                        RaiseMouse(row, Mouse.MouseUpEvent);
                        picker.Result.Should().BeNull("a scrollbar press must not choose an operator");
                        RaiseMouse(row, Mouse.PreviewMouseDownEvent);
                        RaiseMouse(list, Mouse.MouseUpEvent);
                        picker.Result.Should().BeNull("releasing outside a row cancels the click");
                        RaiseMouse(row, Mouse.MouseUpEvent);
                        picker.Result.Should().BeNull("a cancelled press must not leak into a later release");
                        RaiseMouse(row, Mouse.PreviewMouseDownEvent);
                        RaiseMouse(row, Mouse.MouseUpEvent);
                    }
                    picker.Result.Should().Be("拉普兰德");
                }
                finally { picker.Close(); }
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    private static void RaiseMouse(UIElement source, RoutedEvent routedEvent)
        => source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = routedEvent });

    private static void PressEnter(ListBox list, FrameworkElement content)
    {
        list.SelectedItem = list.Items[0];
        list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new PickerPresentationSource(content),
            Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
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

    private sealed class PickerPresentationSource(Visual root) : PresentationSource
    {
        public override Visual RootVisual { get; set; } = root;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget? GetCompositionTargetCore() => null;
    }
}
