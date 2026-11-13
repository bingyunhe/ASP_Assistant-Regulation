using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class CustomOperatorSelectionWindow : Window
{
    private readonly List<CheckBox> _choices = [];
    public IReadOnlyList<string>? Result { get; private set; }

    public CustomOperatorSelectionWindow(IEnumerable<Operator> candidates)
    {
        InitializeComponent();
        foreach (var op in candidates.OrderBy(o => o.Name, StringComparer.CurrentCulture))
        {
            var name = new TextBlock { Text = op.Name, FontSize = 12, FontWeight = FontWeights.Bold };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var covenants = new TextBlock
            {
                Text = string.Join(" · ", op.CoreCovenants.Concat(op.AdditionalCovenants)),
                FontSize = 10, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
            };
            covenants.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var body = new DockPanel { Width = 208 };
            var avatar = new Avatar { Width = 30, Height = 30, Text = op.Name, Source = op.IconPath, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(avatar, Dock.Left);
            body.Children.Add(avatar);
            body.Children.Add(new StackPanel { Children = { name, covenants } });
            var check = new CheckBox { Tag = op.Name, IsChecked = op.IsSelectedCustom, Content = body };
            System.Windows.Automation.AutomationProperties.SetName(check, op.Name);
            _choices.Add(check);
            var card = new Border
            {
                Width = 274, Padding = new Thickness(8), Margin = new Thickness(0, 0, 8, 8),
                BorderThickness = new Thickness(1), Child = check
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            card.SetResourceReference(Border.CornerRadiusProperty, "RowCornerRadius");
            ChoicesPanel.Children.Add(card);
        }
        foreach (var check in _choices)
        {
            check.Checked += (_, _) => UpdateSelection();
            check.Unchecked += (_, _) => UpdateSelection();
        }
        UpdateSelection();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void UpdateSelection()
    {
        var count = _choices.Count(check => check.IsChecked == true);
        foreach (var check in _choices) check.IsEnabled = check.IsChecked == true || count < 12;
        SelectionCount.Text = $"已选择 {count} / 12 名";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        foreach (var check in _choices)
            ((Border)check.Parent).Visibility = ((string)check.Tag).Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = ChoicesPanel.Children.Cast<Border>().Any(card => card.Visibility == Visibility.Visible)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        foreach (var check in _choices) check.IsChecked = false;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = _choices.Where(check => check.IsChecked == true).Select(check => (string)check.Tag).ToList();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
