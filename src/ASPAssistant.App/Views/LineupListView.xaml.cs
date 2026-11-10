using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class LineupListView : UserControl
{
    public event Action? NewLineupRequested;
    public event Action? ImportLineupRequested;
    public event Action<Lineup>? ActiveLineupRequested;
    public event Action<Lineup>? EditLineupRequested;
    public event Action<Lineup>? ShareLineupRequested;
    public event Action<Lineup>? TrackLineupRequested;
    public event Action<Lineup>? DeleteLineupRequested;

    private LineupViewModel? _vm;

    public LineupListView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.Lineups.CollectionChanged -= OnLineupsChanged;

        _vm = e.NewValue as LineupViewModel;
        if (_vm is not null)
            _vm.Lineups.CollectionChanged += OnLineupsChanged;

        UpdateEmptyHint();
    }

    private void OnLineupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateEmptyHint();

    private void UpdateEmptyHint()
    {
        EmptyHint.Visibility = (_vm?.Lineups.Count ?? 0) == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void RefreshAllBadges()
    {
        UpdateEmptyHint();

        // 绑定还没生效（窗口还没显示/还没进过阵容页）时什么都不用做：
        // 早期版本会在这里把 ItemsSource 置 null 再写回，而此时它本来就是 null，
        // 结果把 {Binding Lineups} 表达式直接抹掉了，阵容页从此永远是空的。
        if (LineupItems.ItemsSource is null)
            return;

        // Force-rebuild templates: cheapest reliable way to refresh all the inline badges/icons
        // when a single lineup mutates (e.g. after editor returns).
        var binding = System.Windows.Data.BindingOperations.GetBinding(
            LineupItems, ItemsControl.ItemsSourceProperty);
        LineupItems.ItemsSource = null;
        if (binding is not null)
        {
            // 重新挂回绑定，而不是把旧集合当静态值写回去，否则后续换集合就再也刷新不了。
            System.Windows.Data.BindingOperations.SetBinding(
                LineupItems, ItemsControl.ItemsSourceProperty, binding);
        }
    }

    private void OnNewLineup(object sender, RoutedEventArgs e) => NewLineupRequested?.Invoke();
    private void OnImportLineup(object sender, RoutedEventArgs e) => ImportLineupRequested?.Invoke();

    private Lineup? FindLineup(object sender)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id && _vm is not null)
            return _vm.Lineups.FirstOrDefault(l => l.Id == id);
        return null;
    }

    private void OnActivateLineup(object sender, RoutedEventArgs e)
    {
        if (FindLineup(sender) is { } l) ActiveLineupRequested?.Invoke(l);
    }

    private void OnEditLineup(object sender, RoutedEventArgs e)
    {
        if (FindLineup(sender) is { } l) EditLineupRequested?.Invoke(l);
    }

    private void OnShareLineup(object sender, RoutedEventArgs e)
    {
        if (FindLineup(sender) is { } l) ShareLineupRequested?.Invoke(l);
    }

    private void OnTrackLineup(object sender, RoutedEventArgs e)
    {
        if (FindLineup(sender) is { } l) TrackLineupRequested?.Invoke(l);
    }

    private void OnDeleteLineup(object sender, RoutedEventArgs e)
    {
        if (FindLineup(sender) is { } l) DeleteLineupRequested?.Invoke(l);
    }

    private void OnCovenantBadgeLoaded(object sender, RoutedEventArgs e)
    {
        // Compute activated count for this lineup card.
        if (sender is not TextBlock tb || tb.Tag is not string id || _vm is null) return;
        var lineup = _vm.Lineups.FirstOrDefault(l => l.Id == id);
        if (lineup is null) return;

        int activated = _vm.CountActivated(lineup);
        tb.Text = $"激活 {activated} 个盟约";
    }

    private void OnSlotIconLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image img || img.Tag is not string opName) return;
        if (string.IsNullOrEmpty(opName))
        {
            img.Source = null;
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "data", "icons", "operators", $"{opName}.png");
        if (!File.Exists(iconPath))
        {
            img.Source = null;
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;
        }
        catch
        {
            img.Source = null;
        }
    }
}
