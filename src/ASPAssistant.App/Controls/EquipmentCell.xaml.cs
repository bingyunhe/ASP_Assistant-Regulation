// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Controls;

/// <summary>
/// One equipment item in a two-column list: tier stripe, icon, name, tier / categories, ☆ (or ✕ on the tracking page).
/// </summary>
public partial class EquipmentCell : UserControl
{
    public static readonly DependencyProperty IsAcquiredProperty = DependencyProperty.Register(
        nameof(IsAcquired), typeof(bool), typeof(EquipmentCell), new PropertyMetadata(false,
            (d, _) => ((EquipmentCell)d).UpdateTrackButton()));
    public bool IsAcquired
    {
        get => (bool)GetValue(IsAcquiredProperty);
        set => SetValue(IsAcquiredProperty, value);
    }

    public static readonly RoutedEvent AcquisitionRequestedEvent = EventManager.RegisterRoutedEvent(
        "AcquisitionRequested", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(EquipmentCell));
    public event RoutedEventHandler AcquisitionRequested
    {
        add => AddHandler(AcquisitionRequestedEvent, value);
        remove => RemoveHandler(AcquisitionRequestedEvent, value);
    }

    public static readonly RoutedEvent TrackingToggledEvent = EventManager.RegisterRoutedEvent(
        "TrackingToggled", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(EquipmentCell));

    public event RoutedEventHandler TrackingToggled
    {
        add => AddHandler(TrackingToggledEvent, value);
        remove => RemoveHandler(TrackingToggledEvent, value);
    }

    /// <summary>On the tracking page the button removes the item (✕) and is always shown.</summary>
    public bool IsTrackingList { get; set; }
    public Func<string, bool>? IsTrackedCheck { get; set; }
    public bool IsTracked { get; private set; }

    private static readonly string[] Roman = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];

    public EquipmentCell()
    {
        InitializeComponent();
        HoverToolTipBehavior.Attach(CardBorder, DetailsToolTip);
        CardBorder.ToolTipOpening += (_, _) =>
            DetailsHost.Content = new EquipmentCard { ShowTrackingButton = false, DataContext = DataContext };
        DetailsToolTip.Closed += (_, _) => DetailsHost.Content = null;
        CardBorder.MouseEnter += (_, _) => UpdateTrackButton();
        CardBorder.MouseLeave += (_, _) => UpdateTrackButton();
        DataContextChanged += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            IsTrackedCheck ??= FindAncestor<EquipmentBrowseView>()?.IsTrackedCheck;
            Refresh();
        };
    }

    private void Refresh()
    {
        DetailsToolTip.IsOpen = false;
        if (DataContext is not Equipment eq) return;
        IsTracked = IsTrackingList || IsTrackedCheck?.Invoke(eq.Name) == true;
        var parts = new List<string> { eq.Tier is > 0 and < 7 ? Roman[eq.Tier] : "" };
        if (eq.Type == EquipmentType.JobChange)
            parts.Add(string.IsNullOrEmpty(eq.JobChangeCovenant) ? "转职" : $"转职 {eq.JobChangeCovenant}");
        parts.AddRange(eq.Categories);
        SubText.Text = string.Join(" · ", parts.Where(p => p.Length > 0));
        if (eq.Type == EquipmentType.JobChange) SubText.SetResourceReference(TextBlock.ForegroundProperty, "EliteBrush");
        else SubText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        TierRail.SetResourceReference(Border.BackgroundProperty,
            eq.Tier >= 5 ? "EliteBrush" : eq.Tier == 4 ? "AccentSolidBrush" : "BorderBrush");
        UpdateTrackButton();
    }

    public void RefreshTrackedState(bool isTracked)
    {
        IsTracked = isTracked || IsTrackingList;
        UpdateTrackButton();
    }

    private void UpdateTrackButton()
    {
        AcquireButton.Visibility = IsTrackingList ? Visibility.Visible : Visibility.Collapsed;
        AcquireButton.Content = IsAcquired ? "已获取" : "获取";
        AcquireButton.IsEnabled = !IsAcquired;
        TrackButton.Tag = TryFindResource(IsTrackingList ? "IconClose" : "IconStar");
        TrackButton.ToolTip = IsTrackingList || IsTracked ? "取消追踪装备" : "追踪装备";
        TrackButton.Visibility = IsTrackingList || IsTracked || CardBorder.IsMouseOver ? Visibility.Visible : Visibility.Collapsed;
        var tracked = IsTracked && !IsTrackingList;
        if (tracked)
        {
            TrackButton.SetResourceReference(ForegroundProperty, "FinalBrush");
            TrackButton.SetResourceReference(BackgroundProperty, "FinalTintBrush");
            CardBorder.SetResourceReference(Border.BorderBrushProperty, "FinalBrush");
        }
        else
        {
            TrackButton.ClearValue(ForegroundProperty);
            TrackButton.ClearValue(BackgroundProperty);
            CardBorder.ClearValue(Border.BorderBrushProperty);
        }
    }

    private void OnTrackClick(object sender, RoutedEventArgs e)
    {
        if (!IsTrackingList) IsTracked = !IsTracked;
        UpdateTrackButton();
        RaiseEvent(new RoutedEventArgs(TrackingToggledEvent, this));
    }

    private void OnAcquireClick(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(AcquisitionRequestedEvent, this));

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) => DetailsToolTip.IsOpen = false;

    private T? FindAncestor<T>() where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(this);
        while (current != null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
