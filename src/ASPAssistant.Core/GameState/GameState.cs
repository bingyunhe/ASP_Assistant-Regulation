using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.GameState;

public partial class GameState : ObservableObject
{
    [ObservableProperty]
    private int _currentRound;

    /// <summary>游戏左下角是否已显示「已满级」（满级后不需要再提示升本）。</summary>
    [ObservableProperty]
    private bool _upgradeMaxed;

    [ObservableProperty]
    private int _gold;

    [ObservableProperty]
    private List<(string Name, int Count)> _fieldOperators = [];

    [ObservableProperty]
    private List<(string Name, int Count)> _benchOperators = [];

    [ObservableProperty]
    private List<ShopItem> _shopItems = [];

    [ObservableProperty]
    private Dictionary<string, int> _covenantCounts = new();
}
