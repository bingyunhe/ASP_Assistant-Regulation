using System.ComponentModel;
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
    public event Action<Lineup>? EditLineupRequested;
    public event Action<Lineup>? ShareLineupRequested;
    public event Action<Lineup>? TrackLineupRequested;
    public event Action<Lineup>? DeleteLineupRequested;
    public event Action<Lineup, int>? MoveLineupRequested;

    public static readonly DependencyProperty TrackedLineupIdsProperty = DependencyProperty.Register(
        nameof(TrackedLineupIds), typeof(IReadOnlySet<string>), typeof(LineupListView), new PropertyMetadata(new HashSet<string>()));
    public IReadOnlySet<string> TrackedLineupIds => (IReadOnlySet<string>)GetValue(TrackedLineupIdsProperty);
    private TrackingViewModel? _tracking;

    public void SetTracking(TrackingViewModel tracking)
    {
        if (_tracking is not null) _tracking.PropertyChanged -= OnTrackingChanged;
        _tracking = tracking;
        _tracking.PropertyChanged += OnTrackingChanged;
        SetValue(TrackedLineupIdsProperty, tracking.TrackedLineupIds);
    }

    private void OnTrackingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrackingViewModel.TrackedLineupIds))
            SetValue(TrackedLineupIdsProperty, _tracking!.TrackedLineupIds);
    }

    private LineupViewModel? _vm;

    public LineupListView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnLineupsChanged;

        _vm = e.NewValue as LineupViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnLineupsChanged;

        UpdateEmptyHint();
    }

    private void OnLineupsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LineupViewModel.VisibleLineups)) UpdateEmptyHint();
    }

    private void UpdateEmptyHint()
    {
        EmptyHint.Visibility = (_vm?.VisibleLineups.Count ?? 0) == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        var empty = (_vm?.Lineups.Count ?? 0) == 0;
        EmptyHint.Title = empty ? "还没有阵容" : "没有符合条件的阵容";
        EmptyHint.Hint = empty ? "点右上角“新建”自己搭，或“导入”别人分享的阵容码。规划页的方案也能直接保存过来。"
            : "切换“全部”查看所有阵容；可用性只检查后期阵容。";
    }

    public void RefreshAllBadges()
    {
        _vm?.RefreshAvailability();
    }

    private void OnMoveButtonLoaded(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = _vm!.CanMove((Lineup)button.DataContext, int.Parse((string)button.CommandParameter));
    }

    private void OnMoveLineup(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        MoveLineupRequested?.Invoke((Lineup)button.DataContext, int.Parse((string)button.CommandParameter));
    }

    private void OnClearHighlights(object sender, RoutedEventArgs e) => _tracking!.ClearLineupHighlights();

    private void OnNewLineup(object sender, RoutedEventArgs e) => NewLineupRequested?.Invoke();
    private void OnImportLineup(object sender, RoutedEventArgs e) => ImportLineupRequested?.Invoke();

    private Lineup? FindLineup(object sender)
    {
        if (sender is FrameworkElement { DataContext: Lineup lineup }) return lineup;
        if (sender is FrameworkElement fe && fe.Tag is string id && _vm is not null)
            return _vm.Lineups.FirstOrDefault(l => l.Id == id);
        return null;
    }

    /// <summary>Activated covenants of a card, core first, shown as people ("乌萨斯 6").</summary>
    private void OnCovenantPillsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ItemsControl list || list.DataContext is not Lineup lineup || _vm is null) return;
        list.ItemsSource = _vm.ComputeStats(lineup).Where(c => c.Activated)
            .OrderByDescending(c => c.BondType == "SEASON").ThenByDescending(c => c.Count)
            .Select(c => new LineupCovenantPill(c.Name, c.Count, c.BondType == "SEASON")).ToList();
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

public sealed record LineupCovenantPill(string Name, int Count, bool IsCore);
