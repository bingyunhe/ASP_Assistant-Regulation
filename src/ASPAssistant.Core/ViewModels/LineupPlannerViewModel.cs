using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

/// <summary>One covenant tile on the planner's target page.</summary>
public partial class PlannerCovenantItem : ObservableObject
{
    private readonly Action<PlannerCovenantItem, int> _onToggle;

    public PlannerCovenantItem(CovenantInfo info, Action<PlannerCovenantItem, int> onToggle, bool canTransformInto)
    {
        Info = info;
        _onToggle = onToggle;
        CanTransformInto = canTransformInto;
        HasSecond = LineupPlanner.HasSecondLevel(info);
        SecondManual = LineupPlanner.IsSecondLevelManual(info);
    }

    public CovenantInfo Info { get; }
    public string Name => Info.Name;
    public bool IsCore => Info.BondType == "SEASON";
    public bool HasSecond { get; }
    public bool SecondManual { get; }
    public string SecondLabel => !HasSecond ? "—" : SecondManual ? "Ⅱ*" : "Ⅱ";
    public string TypeText => IsCore ? "核心盟约" : "附加盟约";
    public string FirstRequirement => $"{LineupPlanner.CreateGoal(Info, 1).Count} 人";
    public string SecondRequirement => !HasSecond ? "没有 Ⅱ 级"
        : SecondManual ? $"规划只保证 {LineupPlanner.CreateGoal(Info, 2).Count} 人，叠层 / 精锐等条件需自行确认"
        : $"{LineupPlanner.CreateGoal(Info, 2).Count} 人";
    public bool CanTransformInto { get; }
    /// <summary>In-game effect texts (data/covenant_effects.json).</summary>
    public IReadOnlyList<CovenantEffect> Effects => Info.Effects;
    public bool HasEffects => Info.Effects.Count > 0;

    /// <summary>Explains the ban state in the hover details; empty when not banned.</summary>
    public string BanNote => !IsBanned ? ""
        : IsCore ? "本局已被 Ban，不能作为目标。"
        : "本局已被 Ban：只带被 Ban 盟约的干员被移出，其他持有者仍可凑人数。";
    public bool HasBanNote => IsBanned;

    /// <summary>Operators who hold this covenant natively (tier order); unavailable ones are dimmed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemberSummary))]
    private IReadOnlyList<PlannerCovenantMember> _members = [];

    public string MemberSummary
    {
        get
        {
            var available = Members.Count(m => !m.IsUnavailable);
            return available == Members.Count ? $"持有该盟约的干员 · {Members.Count} 人"
                : $"持有该盟约的干员 · 本局可用 {available} / {Members.Count}";
        }
    }

    public string ToolTip
    {
        get
        {
            if (IsBlocked) return $"{Name}：核心盟约本局已被 Ban";
            var first = LineupPlanner.CreateGoal(Info, 1);
            var text = (IsBanned ? $"{Name} 本局被 Ban，但只带这个盟约的干员才被移出，其他人仍可凑人数\n" : "") + $"{Name} Ⅰ：{first.Count} 人";
            if (!HasSecond) return text + "（没有 Ⅱ 级）";
            var second = LineupPlanner.CreateGoal(Info, 2);
            return text + (SecondManual
                ? $"\nⅡ：只保证 {second.Count} 人，叠层/精锐等条件需人工确认"
                : $"\nⅡ：{second.Count} 人");
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLevel1), nameof(IsLevel2), nameof(IsSelected))]
    private int _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip), nameof(IsEnabled), nameof(IsBlocked), nameof(BanNote), nameof(HasBanNote))]
    private bool _isBanned;

    [ObservableProperty]
    private bool _isAvailable;

    [ObservableProperty]
    private bool _isVisible = true;

    public bool IsLevel1 => Level == 1;
    public bool IsLevel2 => Level == 2;
    public bool IsSelected => Level > 0;
    /// <summary>
    /// A banned core covenant cannot be targeted. A banned additional covenant only removes the operators whose
    /// covenants are all banned, so it can still be reached with the remaining operators and stays selectable.
    /// </summary>
    public bool IsBlocked => IsBanned && IsCore;
    public bool IsEnabled => !IsBlocked;

    [RelayCommand]
    private void ToggleLevel1() => _onToggle(this, 1);

    [RelayCommand]
    private void ToggleLevel2() { if (HasSecond) _onToggle(this, 2); }
}

public sealed partial class PlannerLockedOperator : ObservableObject
{
    public PlannerLockedOperator(Operator op, Action<PlannerLockedOperator> onRemove)
    {
        Operator = op; Name = op.Name; IconPath = op.IconPath;
        RemoveCommand = new RelayCommand(() => onRemove(this));
    }

    public string Name { get; }
    public string IconPath { get; }
    public Operator Operator { get; }
    public string Initial => Name.Length > 0 ? Name[..1] : "?";
    public IRelayCommand RemoveCommand { get; }
}

public sealed class PlannerOperatorItem
{
    public string Name { get; init; } = "";
    public string IconPath { get; init; } = "";
    public string Initial => IsFree ? "+" : Name.Length > 0 ? Name[..1] : "?";
    public string Sub { get; init; } = "";
    public string ToolTip { get; init; } = "";
    public bool IsLocked { get; init; }
    public bool IsTransformed { get; init; }
    public bool IsHarmony { get; init; }
    public bool IsFree { get; init; }
    /// <summary>Full operator data for the hover details (null for free slots).</summary>
    public Operator? Operator { get; init; }
    public bool HasDetails => Operator is not null;
    /// <summary>Planner-specific note shown above the details, e.g. “转职 → 不屈 · 必带”.</summary>
    public string ExtraInfo { get; init; } = "";
    public bool HasExtraInfo => ExtraInfo.Length > 0;
}

/// <summary>An operator who holds a covenant, listed in the covenant hover details.</summary>
public sealed record PlannerCovenantMember(string Name, string IconPath, int Tier, bool IsUnavailable, Operator Operator)
{
    public string Initial => Name.Length > 0 ? Name[..1] : "?";
}

public sealed record PlannerGoalPill(string Text, bool IsManual);

public sealed class PlannerLineupItem
{
    public required PlannedLineup Source { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string? Note { get; init; }
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public IReadOnlyList<PlannerOperatorItem> Operators { get; init; } = [];
    public IReadOnlyList<PlannerGoalPill> Goals { get; init; } = [];
    public string LineupName { get; init; } = "";

    public Lineup ToLineup(LineupEquipmentService equipmentRules) => new()
    {
        Name = LineupName,
        Slots = Source.Operators.Select(o => new LineupSlot
        {
            OperatorName = o.Name,
            Equipments = o.TransformedCovenant is null ? [] :
                [LineupEquipmentService.Shapeshifter, equipmentRules.EquipmentFor(o.TransformedCovenant)]
        }).ToList()
    };
}

public sealed partial class PlannerVariantCell : ObservableObject
{
    public int People { get; init; }
    public int Transforms { get; init; }
    public int Count { get; init; }
    public bool IsLimited { get; init; }
    public bool HasResults => Count > 0;
    public string Text => Count > 0 ? Count.ToString() : "—";
    public string ToolTip => Count > 0
        ? $"{People} 人 · {Transforms} 转：{Count} 套" + (IsLimited ? "（达到搜索上限，可能还有其他组合）" : "")
        : IsLimited ? $"{People} 人 · {Transforms} 转：达到搜索上限仍未找到" : $"{People} 人 · {Transforms} 转：未找到候选";

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// A saved lineup whose activated covenants (late-phase team) are all untouched by the current run's bans.
/// Selectable so the player can track every operator of several surviving teams at once.
/// </summary>
public sealed partial class PlannerSavedLineupItem : ObservableObject
{
    private readonly Action<PlannerSavedLineupItem, bool>? _onSelectionChanged;

    public PlannerSavedLineupItem(Action<PlannerSavedLineupItem, bool>? onSelectionChanged = null)
        => _onSelectionChanged = onSelectionChanged;

    public required Lineup Source { get; init; }
    public string Id => Source.Id;
    public string Name { get; init; } = "";
    public IReadOnlyList<PlannerOperatorItem> Operators { get; init; } = [];
    public IReadOnlyList<PlannerGoalPill> Covenants { get; init; } = [];
    public string Summary { get; init; } = "";
    public string ToolTip { get; init; } = "";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged?.Invoke(this, value);
}

/// <summary>
/// State of the 规划 tab: covenant targets, locked operators, debounced/cancellable search and result browsing.
/// </summary>
public partial class LineupPlannerViewModel : ObservableObject
{
    private static readonly int[] PeopleOptions = [8, 9];
    private static readonly int[] TransformOptions = [0, 1, 2];

    private readonly LineupPlanner _planner;
    private readonly BanViewModel _banVm;
    private readonly LineupViewModel? _lineupVm;
    private readonly IReadOnlyList<Operator> _operators;
    private readonly Dictionary<string, Operator> _operatorByName;
    private IReadOnlySet<string> _availableCovenants = new HashSet<string>();
    private ObservableCollection<Lineup>? _observedLineups;
    private readonly HashSet<string> _selectedSavedLineupIds = new(StringComparer.Ordinal);
    private bool _savedLineupsHaveActivatedTeams;
    private PlannerResult? _lastResult;
    private CancellationTokenSource? _cts;
    private bool _dirty;
    private bool _isActive;

    private readonly LineupEquipmentService _equipmentRules;

    public LineupPlannerViewModel(
        LineupPlanner planner,
        IReadOnlyList<Operator> operators,
        BanViewModel banVm,
        LineupEquipmentService equipmentRules,
        LineupViewModel? lineupVm = null)
    {
        _equipmentRules = equipmentRules;
        _planner = planner;
        _banVm = banVm;
        _lineupVm = lineupVm;
        _operators = operators.Where(o => o.RequiredStrategyId.Length == 0).ToList();
        _operatorByName = _operators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var c in planner.Covenants)
            (c.BondType == "SEASON" ? CoreCovenants : ExtraCovenants).Add(new PlannerCovenantItem(c, OnToggleLevel,
                _equipmentRules.CanTransformInto(c.Name) && !LineupPlanner.NoTransform.Contains(c.Name)));
        foreach (var p in PeopleOptions)
            foreach (var t in TransformOptions)
                VariantCells.Add(new PlannerVariantCell { People = p, Transforms = t });
        if (_lineupVm is not null)
        {
            _lineupVm.PropertyChanged += OnLineupViewModelPropertyChanged;
            ObserveSavedLineups(_lineupVm.Lineups);
        }
        banVm.BansChanged += OnBansChanged;
        ApplyBans();
        UpdateStatus();
    }

    /// <summary>Delay before a search starts after the last change. Tests set 0.</summary>
    public int DebounceMilliseconds { get; set; } = 180;

    /// <summary>Marshals external events (ban changes) onto the UI thread. Null runs them inline.</summary>
    public Action<Action>? RunOnUiThread { get; set; }

    /// <summary>The search currently running (or debouncing); exposed for tests.</summary>
    public Task? PendingSearch { get; private set; }

    public ObservableCollection<PlannerCovenantItem> CoreCovenants { get; } = [];
    public ObservableCollection<PlannerCovenantItem> ExtraCovenants { get; } = [];
    public ObservableCollection<PlannerLockedOperator> LockedOperators { get; } = [];
    public ObservableCollection<Operator> LockSuggestions { get; } = [];
    public ObservableCollection<PlannerVariantCell> VariantCells { get; } = [];
    public ObservableCollection<PlannerLineupItem> VisibleLineups { get; } = [];
    public ObservableCollection<PlannerSavedLineupItem> MatchingLineups { get; } = [];
    public ObservableCollection<string> GoalChips { get; } = [];

    public IEnumerable<PlannerCovenantItem> AllCovenants => CoreCovenants.Concat(ExtraCovenants);

    /// <summary>Raised when the player saves a candidate as a lineup.</summary>
    public event Action<Lineup>? SaveRequested;
    /// <summary>Raised when the player tracks a candidate.</summary>
    public event Action<Lineup>? TrackRequested;
    /// <summary>Raised when the player tracks the saved lineups selected on the ban board.</summary>
    public event Action<IReadOnlyList<Lineup>>? TrackSavedLineupsRequested;
    /// <summary>Supplies the currently tracked operator names for “从追踪导入”.</summary>
    public Func<IEnumerable<string>>? TrackedOperatorsProvider { get; set; }

    [ObservableProperty] private bool _showResults;
    [ObservableProperty] private bool _onlyCurrentRun;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private string _runInfoText = "";
    [ObservableProperty] private bool _hasRunInfo;
    [ObservableProperty] private int _selectedGoalCount;
    [ObservableProperty] private int _totalResults;
    [ObservableProperty] private string _lockSearchText = "";
    [ObservableProperty] private string _resultHint = "";
    [ObservableProperty] private PlannerVariantCell? _selectedVariant;

    public bool HasGoals => SelectedGoalCount > 0;
    public bool HasLocked => LockedOperators.Count > 0;
    public bool HasVisibleLineups => VisibleLineups.Count > 0;
    public bool HasMatchingLineups => MatchingLineups.Count > 0;
    public string MatchingLineupSummary =>
        HasRunInfo && _lineupVm is not null ? $"符合 {MatchingLineups.Count} / {_lineupVm.Lineups.Count}" : "";
    public string MatchingLineupHint
    {
        get
        {
            if (!HasRunInfo)
                return "扫描本局信息后，自动列出后期阵容的激活盟约全部未被禁用的已保存阵容。";
            if (_lineupVm is null || _lineupVm.Lineups.Count == 0)
                return "还没有保存过阵容，先在“阵容”页创建一套队伍。";
            if (MatchingLineups.Count > 0) return "";
            return _savedLineupsHaveActivatedTeams
                ? "没有完全符合的阵容：有激活盟约的阵容都包含被本局禁用的盟约。"
                : "没有可用的已保存阵容：这些阵容的后期阶段还没有激活盟约。";
        }
    }
    public string EmptyResultText => SelectedGoalCount == 0 ? "先在“目标”里选择盟约。"
        : IsBusy ? "正在搜索…"
        : HasError ? StatusText
        : "这一组没有可行组合，可以换一格看看，或减少目标、增加转职。";
    public int SelectedMatchingLineupCount => MatchingLineups.Count(l => l.IsSelected);
    public bool HasSelectedMatchingLineups => SelectedMatchingLineupCount > 0;
    public string TrackSelectedText => SelectedMatchingLineupCount > 0
        ? $"追踪已选阵容 ({SelectedMatchingLineupCount})"
        : "追踪已选阵容";
    public string SelectAllMatchingText =>
        MatchingLineups.Count > 0 && MatchingLineups.All(l => l.IsSelected) ? "取消全选" : "全选";
    public string GoalTabText => SelectedGoalCount > 0 ? $"目标 · 已选 {SelectedGoalCount}" : "目标";
    public string ResultTabText => TotalResults > 0 ? $"结果 {TotalResults}" : "结果";
    public string ViewResultsText => TotalResults > 0 ? $"查看 {TotalResults} 套 →" : "查看结果 →";

    /// <summary>Whether the 规划 tab is on screen. Changes while hidden only mark the results stale.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            if (value && _dirty) RequestRecompute();
        }
    }

    partial void OnSelectedGoalCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasGoals));
        OnPropertyChanged(nameof(GoalTabText));
        OnPropertyChanged(nameof(EmptyResultText));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(EmptyResultText));
    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(EmptyResultText));

    partial void OnTotalResultsChanged(int value)
    {
        OnPropertyChanged(nameof(ResultTabText));
        OnPropertyChanged(nameof(ViewResultsText));
    }

    partial void OnOnlyCurrentRunChanged(bool value) => ApplyVisibility();

    partial void OnLockSearchTextChanged(string value)
    {
        LockSuggestions.Clear();
        var text = value.Trim();
        if (text.Length == 0) return;
        foreach (var op in _operators
                     .Where(o => o.Name.Contains(text, StringComparison.OrdinalIgnoreCase) && LockedOperators.All(l => l.Name != o.Name))
                     .OrderBy(o => o.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(o => o.Name.Length)
                     .Take(8))
            LockSuggestions.Add(op);
    }

    partial void OnSelectedVariantChanged(PlannerVariantCell? value)
    {
        foreach (var cell in VariantCells) cell.IsSelected = ReferenceEquals(cell, value);
        RebuildVisibleLineups();
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ClearGoals()
    {
        foreach (var c in AllCovenants) c.Level = 0;
        GoalsChanged();
    }

    [RelayCommand] private void ShowGoalPage() => ShowResults = false;
    [RelayCommand] private void ShowResultPage() => ShowResults = true;

    [RelayCommand]
    private void SelectVariant(PlannerVariantCell? cell)
    {
        if (cell is not null) SelectedVariant = cell;
    }

    /// <summary>Locks the exact match, or the first suggestion, of <paramref name="name"/>.</summary>
    [RelayCommand]
    private void LockOperator(string? name) => AddLocked(name);

    public bool AddLocked(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return false;
        if (!_operatorByName.TryGetValue(name, out var op))
        {
            op = LockSuggestions.FirstOrDefault();
            if (op is null) return false;
        }
        if (LockedOperators.Any(l => l.Name == op.Name)) { LockSearchText = ""; return false; }
        if (LockedOperators.Count >= LineupPlanner.MaxLockedOperators) return false;
        LockedOperators.Add(new PlannerLockedOperator(op, RemoveLocked));
        LockSearchText = "";
        OnPropertyChanged(nameof(HasLocked));
        RequestRecompute();
        return true;
    }

    [RelayCommand]
    private void ImportTracked()
    {
        var names = TrackedOperatorsProvider?.Invoke()?.ToList() ?? [];
        var changed = false;
        foreach (var name in names)
        {
            if (LockedOperators.Count >= LineupPlanner.MaxLockedOperators) break;
            if (!_operatorByName.TryGetValue(name, out var op) || LockedOperators.Any(l => l.Name == name)) continue;
            LockedOperators.Add(new PlannerLockedOperator(op, RemoveLocked));
            changed = true;
        }
        if (!changed) return;
        OnPropertyChanged(nameof(HasLocked));
        RequestRecompute();
    }

    [RelayCommand]
    private void ClearLocked()
    {
        if (LockedOperators.Count == 0) return;
        LockedOperators.Clear();
        OnPropertyChanged(nameof(HasLocked));
        RequestRecompute();
    }

    [RelayCommand]
    private void SaveLineup(PlannerLineupItem? item)
    {
        if (item is not null) SaveRequested?.Invoke(item.ToLineup(_equipmentRules));
    }

    [RelayCommand]
    private void TrackLineup(PlannerLineupItem? item)
    {
        if (item is not null) TrackRequested?.Invoke(item.ToLineup(_equipmentRules));
    }

    [RelayCommand]
    private void ToggleSelectAllMatching()
    {
        var select = !MatchingLineups.All(l => l.IsSelected);
        foreach (var item in MatchingLineups) item.IsSelected = select;
        NotifyMatchingSelectionChanged();
    }

    [RelayCommand]
    private void TrackSelectedSavedLineups()
    {
        var selected = MatchingLineups.Where(l => l.IsSelected).Select(l => l.Source).ToList();
        if (selected.Count == 0) return;
        TrackSavedLineupsRequested?.Invoke(selected);
    }

    private void OnSavedLineupSelectionChanged(PlannerSavedLineupItem item, bool selected)
    {
        if (!MatchingLineups.Contains(item)) return;
        if (selected) _selectedSavedLineupIds.Add(item.Id);
        else _selectedSavedLineupIds.Remove(item.Id);
        NotifyMatchingSelectionChanged();
    }

    private void NotifyMatchingSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedMatchingLineupCount));
        OnPropertyChanged(nameof(HasSelectedMatchingLineups));
        OnPropertyChanged(nameof(TrackSelectedText));
        OnPropertyChanged(nameof(SelectAllMatchingText));
    }

    private void RemoveLocked(PlannerLockedOperator item)
    {
        if (!LockedOperators.Remove(item)) return;
        OnPropertyChanged(nameof(HasLocked));
        RequestRecompute();
    }

    // ── External inputs ─────────────────────────────────────────────────────

    /// <summary>Covenants recognised as available in the current run (from the ban screen).</summary>
    public void SetAvailableCovenants(IEnumerable<string> names)
    {
        _availableCovenants = names.ToHashSet(StringComparer.Ordinal);
        if (_availableCovenants.Count == 0) OnlyCurrentRun = false;
        foreach (var c in AllCovenants) c.IsAvailable = _availableCovenants.Contains(c.Name);
        ApplyVisibility();
        UpdateRunInfo();
        RefreshMatchingLineups();
    }

    private void OnBansChanged()
    {
        if (RunOnUiThread is { } dispatch)
        {
            dispatch(() => { ApplyBans(); RequestRecompute(); });
            return;
        }
        ApplyBans();
        RequestRecompute();
    }

    private void ApplyBans()
    {
        var goalDropped = false;
        foreach (var c in AllCovenants)
        {
            c.IsBanned = _banVm.IsCovenantBanned(c.Name);
            if (c.IsBlocked && c.Level > 0) { c.Level = 0; goalDropped = true; }
        }
        if (goalDropped) RefreshGoalChips();
        RefreshMembers();
        UpdateRunInfo();
        RefreshMatchingLineups();
    }

    private void RefreshMembers()
    {
        var bannedCovenants = _banVm.BannedCovenants;
        foreach (var c in AllCovenants)
        {
            c.Members = _operators
                .Where(o => o.CoreCovenants.Contains(c.Name) || o.AdditionalCovenants.Contains(c.Name))
                .OrderBy(o => o.Tier).ThenBy(o => o.Name, StringComparer.Ordinal)
                .Select(o => new PlannerCovenantMember(o.Name, o.IconPath, o.Tier,
                    _banVm.IsBanned(o.Name) || _planner.IsExcludedByBans(o, bannedCovenants), o))
                .ToList();
        }
    }

    // ── Saved lineups that survive the current bans ─────────────────────────

    private void OnLineupViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LineupViewModel.Lineups)) return;
        ObserveSavedLineups(_lineupVm!.Lineups);
        RefreshMatchingLineups();
    }

    private void ObserveSavedLineups(ObservableCollection<Lineup>? lineups)
    {
        if (ReferenceEquals(_observedLineups, lineups)) return;
        if (_observedLineups is not null)
            _observedLineups.CollectionChanged -= OnSavedLineupsChanged;
        _observedLineups = lineups;
        if (_observedLineups is not null)
            _observedLineups.CollectionChanged += OnSavedLineupsChanged;
    }

    private void OnSavedLineupsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshMatchingLineups();

    /// <summary>
    /// Rebuilds the saved-lineup board: keep a lineup only when every covenant activated by its
    /// late-phase team (<see cref="Lineup.Slots"/>) is untouched by the current run's covenant bans.
    /// Nothing is listed until the run's ban information has been recorded.
    /// </summary>
    public void RefreshMatchingLineups()
    {
        MatchingLineups.Clear();
        _savedLineupsHaveActivatedTeams = false;
        if (_lineupVm is not null && HasRunInfo)
        {
            var banned = _banVm.BannedCovenants;
            foreach (var lineup in _lineupVm.Lineups)
            {
                var activated = _lineupVm.Calculator.Compute(lineup).Where(s => s.Activated).ToList();
                if (activated.Count == 0) continue;
                _savedLineupsHaveActivatedTeams = true;
                if (activated.Any(s => banned.Contains(s.Name))) continue;
                MatchingLineups.Add(BuildSavedLineupItem(lineup, activated));
            }
        }
        OnPropertyChanged(nameof(HasMatchingLineups));
        OnPropertyChanged(nameof(MatchingLineupHint));
        OnPropertyChanged(nameof(MatchingLineupSummary));
        NotifyMatchingSelectionChanged();
    }

    private PlannerSavedLineupItem BuildSavedLineupItem(Lineup lineup, IReadOnlyList<CovenantStat> activated)
    {
        var ops = new List<PlannerOperatorItem>();
        foreach (var slot in lineup.Slots)
        {
            if (string.IsNullOrEmpty(slot.OperatorName)) continue;
            _operatorByName.TryGetValue(slot.OperatorName, out var op);
            var tip = op is null
                ? $"{slot.OperatorName}\n干员资料未收录"
                : $"{slot.OperatorName}\n原生：{string.Join("、", op.CoreCovenants.Concat(op.AdditionalCovenants))}";
            var transformations = _equipmentRules.Transformations(slot);
            if (transformations.Count > 0)
                tip += $"\n转职 → {string.Join("、", transformations.Select(t => t.Covenant))}";
            ops.Add(new PlannerOperatorItem
            {
                Name = slot.OperatorName,
                IconPath = op?.IconPath ?? "",
                Operator = op,
                ToolTip = tip,
                Sub = transformations.Count > 0
                    ? $"转→{string.Join("、", transformations.Select(t => t.Covenant))}"
                    : "",
            });
        }
        var chips = activated
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new PlannerGoalPill($"{s.Name} {s.Count}/{s.ActivateCount}", false))
            .ToList();
        var covenantText = string.Join("、", chips.Select(c => c.Text));
        var displayName = string.IsNullOrWhiteSpace(lineup.Name) ? "未命名阵容" : lineup.Name;
        return new PlannerSavedLineupItem(OnSavedLineupSelectionChanged)
        {
            Source = lineup,
            IsSelected = _selectedSavedLineupIds.Contains(lineup.Id),
            Name = displayName,
            Operators = ops,
            Covenants = chips,
            Summary = $"{ops.Count} 人 · {activated.Count} 个盟约未禁用",
            ToolTip = $"{displayName}\n后期阵容的激活盟约：{covenantText}",
        };
    }

    private void ApplyVisibility()
    {
        var filter = OnlyCurrentRun && _availableCovenants.Count > 0;
        foreach (var c in AllCovenants)
            c.IsVisible = !filter || c.IsAvailable || c.IsSelected || (c.IsBanned && !c.IsBlocked);
    }

    private void UpdateRunInfo()
    {
        var banned = AllCovenants.Count(c => c.IsBanned);
        HasRunInfo = _availableCovenants.Count > 0 || banned > 0;
        RunInfoText = HasRunInfo
            ? $"已读取本局盟约 · {banned} 个被 Ban"
            : "未录入本局信息 · 开局信息页可截屏录入";
        OnPropertyChanged(nameof(MatchingLineupHint));
        OnPropertyChanged(nameof(MatchingLineupSummary));
    }

    private void OnToggleLevel(PlannerCovenantItem item, int level)
    {
        if (item.IsBlocked) return;
        item.Level = item.Level == level ? 0 : level;
        GoalsChanged();
    }

    private void GoalsChanged()
    {
        RefreshGoalChips();
        ApplyVisibility();
        RequestRecompute();
    }

    private void RefreshGoalChips()
    {
        GoalChips.Clear();
        foreach (var c in AllCovenants.Where(c => c.IsSelected))
            GoalChips.Add($"{c.Name} {(c.Level == 2 ? c.SecondLabel : "Ⅰ")}");
        SelectedGoalCount = GoalChips.Count;
    }

    // ── Search pipeline ─────────────────────────────────────────────────────

    public PlannerRequest BuildRequest()
    {
        var bannedCovenants = new HashSet<string>(_banVm.BannedCovenants, StringComparer.Ordinal);
        return new PlannerRequest
        {
            Goals = AllCovenants.Where(c => c.IsSelected).Select(c => LineupPlanner.CreateGoal(c.Info, c.Level)).ToList(),
            BannedCovenants = bannedCovenants,
            BannedOperators = new HashSet<string>(_banVm.BannedNames, StringComparer.Ordinal),
            LockedOperators = LockedOperators.Select(l => l.Name).ToHashSet(StringComparer.Ordinal),
            TransformAllowed = _planner.DefaultTransformAllowed(bannedCovenants).Where(_equipmentRules.CanTransformInto).ToHashSet(),
            PeopleOptions = PeopleOptions,
            TransformOptions = TransformOptions
        };
    }

    /// <summary>Cancels any running search and schedules a new one (debounced). Deferred while the tab is hidden.</summary>
    public void RequestRecompute()
    {
        _cts?.Cancel();
        _cts = null;
        if (!IsActive) { _dirty = true; return; }
        _dirty = false;

        var request = BuildRequest();
        if (request.Goals.Count == 0)
        {
            _lastResult = null;
            IsBusy = false;
            ApplyResult(null, TimeSpan.Zero);
            PendingSearch = Task.CompletedTask;
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        PendingSearch = RunSearchAsync(request, cts);
    }

    private async Task RunSearchAsync(PlannerRequest request, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            if (DebounceMilliseconds > 0) await Task.Delay(DebounceMilliseconds, token);
            IsBusy = true;
            StatusText = "正在搜索…";
            StatusDetail = "";
            var sw = Stopwatch.StartNew();
            var result = await Task.Run(() => _planner.Plan(request, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(_cts, cts)) return;
            _lastResult = result;
            ApplyResult(result, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search.
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_cts, cts)) return;
            HasError = true;
            StatusText = "规划出错";
            StatusDetail = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_cts, cts)) IsBusy = false;
        }
    }

    private void ApplyResult(PlannerResult? result, TimeSpan elapsed)
    {
        var previous = SelectedVariant;
        VariantCells.Clear();
        foreach (var p in PeopleOptions)
            foreach (var t in TransformOptions)
            {
                var v = result?.Variants.FirstOrDefault(x => x.People == p && x.Transforms == t);
                VariantCells.Add(new PlannerVariantCell
                {
                    People = p, Transforms = t, Count = v?.Lineups.Count ?? 0, IsLimited = v?.SearchLimited ?? false
                });
            }
        TotalResults = VariantCells.Sum(c => c.Count);

        SelectedVariant =
            VariantCells.FirstOrDefault(c => previous is not null && c.People == previous.People && c.Transforms == previous.Transforms && c.HasResults)
            ?? VariantCells.FirstOrDefault(c => c.HasResults)
            ?? VariantCells.FirstOrDefault();
        UpdateStatus(result, elapsed);
    }

    private void UpdateStatus(PlannerResult? result = null, TimeSpan elapsed = default)
    {
        HasError = result?.Error is not null;
        if (SelectedGoalCount == 0)
        {
            StatusText = "请选择目标盟约";
            StatusDetail = "点击 Ⅰ / Ⅱ 选择档位，再点一次取消";
            ResultHint = "";
            return;
        }
        if (result?.Error is { } error)
        {
            StatusText = error;
            StatusDetail = "";
            ResultHint = error;
            return;
        }
        if (result is null) return;

        var solved = VariantCells.Count(c => c.HasResults);
        StatusText = solved == 0 ? "没有找到可行组合" : $"✓ {VariantCells.Count} 组中 {solved} 组有解";
        var detail = $"{elapsed.TotalSeconds:0.0} 秒 · 改动后自动重算";
        if (VariantCells.Any(c => c.IsLimited)) detail += " · 部分组达到搜索上限";
        StatusDetail = detail;

        var unsolved = VariantCells.Where(c => !c.HasResults)
            .Select(c => $"{c.People} 人·{c.Transforms} 转" + (c.IsLimited ? "（搜索受限）" : "")).ToList();
        var hint = unsolved.Count == 0 ? "" : $"尚未找到候选：{string.Join("、", unsolved)}";
        if (result.Variants.SelectMany(v => v.Lineups).Any(l => l.Goals.Any(g => g.Manual)))
            hint += (hint.Length > 0 ? "；" : "") + "Ⅱ* 只保证人数，叠层/精锐条件需自行确认";
        ResultHint = hint;
    }

    private void RebuildVisibleLineups()
    {
        VisibleLineups.Clear();
        var cell = SelectedVariant;
        var variant = _lastResult?.Variants.FirstOrDefault(v => cell is not null && v.People == cell.People && v.Transforms == cell.Transforms);
        if (variant is not null)
        {
            var index = 0;
            foreach (var lineup in variant.Lineups) VisibleLineups.Add(BuildItem(lineup, index++));
        }
        OnPropertyChanged(nameof(HasVisibleLineups));
    }

    private PlannerLineupItem BuildItem(PlannedLineup lineup, int index)
    {
        var goalNames = lineup.Goals.Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
        var ops = lineup.Operators.Select(o =>
        {
            string sub;
            if (o.TransformedCovenant is not null) sub = $"转→{o.TransformedCovenant}";
            else
            {
                var relevant = o.NativeCovenants.Where(goalNames.Contains).ToList();
                if (o.IsHarmony && relevant.Count == 0) relevant.Add(CovenantCalculator.HarmonyCovenant);
                sub = string.Join(" ", relevant.Count > 0 ? relevant : o.NativeCovenants.Take(1));
            }
            var tip = $"{o.Name}\n原生：{string.Join("、", o.NativeCovenants)}";
            if (o.TransformedCovenant is not null) tip += $"\n转入：{o.TransformedCovenant}";
            if (o.IsLocked) tip += "\n必带干员";
            var extra = new List<string>();
            if (o.TransformedCovenant is not null) extra.Add($"转职 → {o.TransformedCovenant}");
            if (o.IsHarmony) extra.Add("调和：阵容内已有的核心盟约各 +1");
            if (o.IsLocked) extra.Add("必带干员");
            return new PlannerOperatorItem
            {
                Name = o.Name, IconPath = o.IconPath, Sub = sub, ToolTip = tip,
                IsLocked = o.IsLocked, IsTransformed = o.TransformedCovenant is not null, IsHarmony = o.IsHarmony,
                Operator = _operatorByName.GetValueOrDefault(o.Name),
                ExtraInfo = string.Join(" · ", extra)
            };
        }).ToList();
        for (var i = 0; i < lineup.FreeSlots; i++)
            ops.Add(new PlannerOperatorItem { Name = "空位", Sub = "任选", IsFree = true, ToolTip = "目标已满足，这个位置可以放任意干员" });

        var goals = lineup.Goals.Select(g =>
        {
            lineup.Counts.TryGetValue(g.Name, out var count);
            var text = $"{g.Name} {count}/{g.Count} ✓";
            if (lineup.HarmonyBonus.TryGetValue(g.Name, out var bonus) && bonus > 0) text += $" 含调和+{bonus}";
            if (g.Manual) text += " · Ⅱ待核";
            return new PlannerGoalPill(text, g.Manual);
        }).ToList();

        var subtitle = $"{lineup.People} 人 · {lineup.Transforms} 转";
        if (lineup.FreeSlots > 0) subtitle += $" · 空 {lineup.FreeSlots}";
        var goalText = string.Join(" ", lineup.Goals.Select(g => g.Name + (g.Level == 2 ? "Ⅱ" : "Ⅰ")));
        return new PlannerLineupItem
        {
            Source = lineup,
            Title = $"方案 {(char)('A' + index)}",
            Subtitle = subtitle,
            Note = lineup.ForcedTransforms > 0 ? $"含 {lineup.ForcedTransforms} 次非必要转职（只为凑满转职次数）" : null,
            Operators = ops,
            Goals = goals,
            LineupName = $"规划 {goalText} {lineup.People}人{lineup.Transforms}转"
        };
    }
}
