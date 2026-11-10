using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class GameStateViewModel : ObservableObject
{
    [ObservableProperty]
    private int _currentRound;

    [ObservableProperty]
    private bool _upgradeMaxed;

    [ObservableProperty]
    private int _gold;

    [ObservableProperty]
    private ObservableCollection<string> _fieldOperatorSummary = [];

    [ObservableProperty]
    private ObservableCollection<string> _benchOperatorSummary = [];

    [ObservableProperty]
    private ObservableCollection<ShopItem> _shopItems = [];

    [ObservableProperty]
    private ObservableCollection<string> _trackingAlerts = [];

    /// <summary>追踪页显示的回合文案；未识别时给出可读提示而不是 0。</summary>
    public string RoundDisplay => CurrentRound > 0 ? $"第 {CurrentRound} 回合" : "未识别";

    /// <summary>升本按钮状态：已满级 / 可升级 / 未识别。</summary>
    public string UpgradeStateDisplay => UpgradeMaxed ? "已满级" : CurrentRound > 0 ? "可升级" : "未识别";

    partial void OnCurrentRoundChanged(int value)
    {
        OnPropertyChanged(nameof(RoundDisplay));
        OnPropertyChanged(nameof(UpgradeStateDisplay));
    }

    partial void OnUpgradeMaxedChanged(bool value) => OnPropertyChanged(nameof(UpgradeStateDisplay));

    public void UpdateFromGameState(Core.GameState.GameState state)
    {
        CurrentRound = state.CurrentRound;
        UpgradeMaxed = state.UpgradeMaxed;
        Gold = state.Gold;

        FieldOperatorSummary = new ObservableCollection<string>(
            state.FieldOperators.Select(o => $"{o.Name}×{o.Count}"));

        BenchOperatorSummary = new ObservableCollection<string>(
            state.BenchOperators.Select(o => $"{o.Name}×{o.Count}"));

        ShopItems = new ObservableCollection<ShopItem>(state.ShopItems);

        TrackingAlerts = new ObservableCollection<string>(
            state.ShopItems.Where(s => s.IsTracked).Select(s => $"⚠ {s.Name} 在商店!"));
    }
}
