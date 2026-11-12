using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

/// <summary>规划 tab. All state lives in <see cref="LineupPlannerViewModel"/>.</summary>
public partial class LineupPlannerView : UserControl
{
    public LineupPlannerView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => SyncActive();
        DataContextChanged += (_, _) => SyncActive();
    }

    private LineupPlannerViewModel? Vm => DataContext as LineupPlannerViewModel;

    /// <summary>Only search while the tab is on screen; changes made elsewhere are applied on return.</summary>
    private void SyncActive()
    {
        if (Vm is { } vm) vm.IsActive = IsVisible;
    }

    /// <summary>The whole saved-lineup card is the selection target; there is no separate checkbox.</summary>
    private void OnSavedLineupCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlannerSavedLineupItem item })
        {
            item.IsSelected = !item.IsSelected;
            e.Handled = true;
        }
    }

    private void OnLockSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        switch (e.Key)
        {
            case Key.Enter:
                vm.AddLocked(LockSearchBox.Text);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.LockSearchText = "";
                e.Handled = true;
                break;
        }
    }
}
