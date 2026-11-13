using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

/// <summary>
/// 已保存阵容列表 + 增删改 + 激活盟约统计访问。
/// </summary>
public partial class LineupViewModel : ObservableObject
{
    private readonly LineupStore _store;
    private Func<string, bool> _isBanned = _ => false;
    public CovenantCalculator Calculator { get; }

    public ObservableCollection<Lineup> Lineups { get; } = [];

    [ObservableProperty]
    private AvailabilityFilter _selectedAvailabilityFilter;

    public IReadOnlyList<Lineup> VisibleLineups => [.. Lineups.Where(lineup => SelectedAvailabilityFilter == AvailabilityFilter.All
        || lineup.Slots.All(slot => !_isBanned(slot.OperatorName)) == (SelectedAvailabilityFilter == AvailabilityFilter.Available))];

    public LineupViewModel(LineupStore store, CovenantCalculator calculator)
    {
        _store = store;
        Calculator = calculator;
        Lineups.CollectionChanged += (_, _) => RefreshAvailability();
    }

    public void LoadLineups(IEnumerable<Lineup> lineups)
    {
        Lineups.Clear();
        foreach (var lineup in lineups) Lineups.Add(lineup);
    }

    public void SetBanChecker(Func<string, bool> checker)
    {
        _isBanned = checker;
        RefreshAvailability();
    }

    public void RefreshAvailability() => OnPropertyChanged(nameof(VisibleLineups));

    partial void OnSelectedAvailabilityFilterChanged(AvailabilityFilter value) => RefreshAvailability();

    public bool CanMove(Lineup lineup, int direction)
    {
        var index = VisibleLineups.ToList().IndexOf(lineup);
        return index >= 0 && (direction == 0 ? Lineups.IndexOf(lineup) > 0
            : direction < 0 ? index > 0 : index < VisibleLineups.Count - 1);
    }

    public async Task MoveAsync(Lineup lineup, int direction)
    {
        if (!CanMove(lineup, direction)) return;
        var original = Lineups.ToList();
        var index = Lineups.IndexOf(lineup);
        if (direction == 0) Lineups.Move(index, 0);
        else
        {
            var visible = VisibleLineups.ToList();
            var other = visible[visible.IndexOf(lineup) + direction];
            var otherIndex = Lineups.IndexOf(other);
            Lineups[index] = other;
            Lineups[otherIndex] = lineup;
        }
        try { await _store.SaveOrderAsync(Lineups.Select(l => l.Id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadLineups(original);
            throw;
        }
    }

    /// <summary>新增或更新阵容，并落盘。</summary>
    public async Task AddOrUpdateAsync(Lineup lineup)
    {
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        lineup.UpdatedAt = DateTime.UtcNow;

        await _store.SaveAsync(lineup);

        var existing = Lineups.FirstOrDefault(l => l.Id == lineup.Id);
        if (existing is not null)
        {
            int idx = Lineups.IndexOf(existing);
            Lineups[idx] = lineup;
        }
        else
        {
            Lineups.Insert(0, lineup);
            await _store.SaveOrderAsync(Lineups.Select(l => l.Id));
        }
    }

    public async Task DeleteAsync(Lineup lineup)
    {
        await _store.DeleteAsync(lineup.Id);
        Lineups.Remove(lineup);
        await _store.SaveOrderAsync(Lineups.Select(l => l.Id));
    }

    public int CountActivated(Lineup lineup) => Calculator.CountActivated(lineup);

    public IReadOnlyList<CovenantStat> ComputeStats(Lineup lineup) => Calculator.Compute(lineup);
}
