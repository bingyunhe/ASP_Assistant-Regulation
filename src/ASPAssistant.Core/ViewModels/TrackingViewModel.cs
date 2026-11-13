// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

public partial class TrackingViewModel : ObservableObject
{
    private DeployPoolState? _deployPool;
    private List<TrackedLineup> _trackedLineups = [];
    public IReadOnlySet<string> TrackedLineupIds => _trackedLineups.Where(l => l.Highlighted).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
    public bool IsLineupTracked(string id) => _trackedLineups.Any(l => l.Id == id);
    public event Action? SourcesChanged;

    public TrackingState CaptureState() => new() { Entries = [.. AllEntries], Lineups = [.. _trackedLineups] };

    public void ClearLineupHighlights()
    {
        _trackedLineups = _trackedLineups.Select(l => l with { Highlighted = false }).ToList();
        NotifySourcesChanged();
    }

    private void NotifySourcesChanged()
    {
        var ids = TrackedOperators.SelectMany(e => e.Origins).Select(o => o.LineupId).ToHashSet();
        _trackedLineups.RemoveAll(l => !ids.Contains(l.Id));
        OnPropertyChanged(nameof(TrackedLineupIds));
        SourcesChanged?.Invoke();
    }

    private static TrackingEntry MergeOrigins(TrackingEntry entry, List<TrackingOrigin> origins)
    {
        var preferred = origins.LastOrDefault(o => o.Role == TrackingRole.Final) ?? origins[^1];
        var merged = Copy(entry, false);
        merged.Origins = origins;
        merged.IsManual = origins.Any(o => o.LineupId.Length == 0);
        merged.Role = preferred.Role;
        merged.IsElite = origins.Any(o => o.IsElite);
        merged.TransformedCovenant = preferred.TransformedCovenant;
        merged.Equipments = origins.SelectMany(o => o.Equipments).Distinct().ToList();
        merged.Stages = preferred.Role == TrackingRole.Final ? "" : string.Join(" · ", origins.Select(o => o.Stages).Where(s => s.Length > 0).Distinct());
        merged.ExpiresAt = merged.Role == TrackingRole.Final || origins.Any(o => o.ExpiresAt == 0) ? 0 : origins.Max(o => o.ExpiresAt);
        return merged;
    }

    private void MergeIndependentTarget(TrackingEntry entry)
    {
        var existing = TrackedOperators.FirstOrDefault(e => e.Name == entry.Name);
        var origins = existing?.Origins.Where(o => !o.IsRoute).ToList() ?? [];
        if (existing is { Origins.Count: 0 }) origins.Add(TrackingOrigin.From(existing, ""));
        if (entry.Role == TrackingRole.Transition && existing is not null)
            origins.AddRange(existing.Origins.Where(o => o.IsRoute && o.Role == TrackingRole.Final));
        origins.Add(TrackingOrigin.From(entry, "") with { IsRoute = true });
        var merged = MergeOrigins(entry, origins);
        merged.IsExpired = merged.ExpiredAt(TerminalLevel);
        if (existing is null) TrackedOperators.Add(merged);
        else TrackedOperators[TrackedOperators.IndexOf(existing)] = merged;
    }

    [ObservableProperty]
    private bool _trackMultipleLineups;

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedOperators = [];

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedEquipment = [];

    [ObservableProperty]
    private Dictionary<string, int> _covenantCounts = new();

    [ObservableProperty]
    private object? _gameState;

    public IReadOnlyList<TrackingEntry> AllEntries =>
        [.. TrackedOperators, .. TrackedEquipment];

    /// <summary>Entries the shop should mark: 过渡 operators whose route stages are behind the player are left out.</summary>
    public IReadOnlyList<TrackingEntry> ActiveEntries => [.. AllEntries.Where(e => !e.IsExpired)];

    // ── Terminal level ──────────────────────────────────────────────────────

    /// <summary>Level read from the shop's upgrade card (null = not seen yet this run).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalLevel), nameof(TerminalLevelText))]
    private int? _detectedTerminalLevel;

    /// <summary>True while the level is the one saved by the last session and the shop has not shown it yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalLevelText))]
    private bool _isTerminalLevelRestored;

    /// <summary>Player override (null = 自动).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalLevel), nameof(TerminalLevelText), nameof(TerminalLevelChoice))]
    private int? _manualTerminalLevel;

    /// <summary>Effective level; 0 = unknown (nothing expires).</summary>
    public int TerminalLevel => ManualTerminalLevel ?? DetectedTerminalLevel ?? 0;

    public string TerminalLevelText => TerminalLevel == 0 ? "终端等级：未识别"
        : ManualTerminalLevel is not null ? $"终端 {TerminalLevel} 级（手动）"
        : IsTerminalLevelRestored ? $"终端 {TerminalLevel} 级（上次记录，打开商店后确认）"
        : $"终端 {TerminalLevel} 级（自动识别）";

    /// <summary>Choices for the override box: 0 = 自动, 1–6.</summary>
    public IReadOnlyList<int> TerminalLevelChoices { get; } = [0, 1, 2, 3, 4, 5, 6];

    public int TerminalLevelChoice
    {
        get => ManualTerminalLevel ?? 0;
        set => ManualTerminalLevel = value is >= 1 and <= 6 ? value : null;
    }

    partial void OnDetectedTerminalLevelChanged(int? value)
    {
        // The terminal only goes up within a run: once the reading catches up with a manual value, the override has
        // done its job and automatic tracking takes over again (otherwise it would pin the level for the whole run).
        if (ManualTerminalLevel is { } manual && value >= manual) ManualTerminalLevel = null;
        RefreshExpiry();
    }
    partial void OnManualTerminalLevelChanged(int? value) => RefreshExpiry();

    /// <summary>New run: forget the level read in the previous one.</summary>
    public void ResetTerminalLevel()
    {
        ManualTerminalLevel = null;
        DetectedTerminalLevel = null;
        IsTerminalLevelRestored = false;
    }

    public void StartNewRun()
    {
        if (_deployPool is not null)
        {
            _deployPool.Owned.Clear();
            _deployPool.Elite.Clear();
            _deployPool.Counts.Clear();
        }
        ResetTerminalLevel();
        foreach (var entry in AllEntries.Where(e => e.IsAcquired).ToList())
        {
            var entries = entry.Type == TrackingType.Operator ? TrackedOperators : TrackedEquipment;
            var pending = Copy(entry, entry.IsExpired);
            pending.IsAcquired = false;
            entries[entries.IndexOf(entry)] = pending;
        }
        RefreshEquipmentRequirements();
        SyncDeployPool();
    }

    /// <summary>Marks 过渡 entries whose stages are behind the current terminal level (replaced so lists and saves refresh).</summary>
    private void RefreshExpiry()
    {
        var level = TerminalLevel;
        for (var i = 0; i < TrackedOperators.Count; i++)
        {
            var e = TrackedOperators[i];
            var expired = e.ExpiredAt(level);
            if (e.IsExpired == expired) continue;
            TrackedOperators[i] = Copy(e, expired);
        }
        OnPropertyChanged(nameof(ActiveEntries));
    }

    private static TrackingEntry Copy(TrackingEntry e, bool expired) => new()
    {
        Name = e.Name, Type = e.Type, TransformedCovenant = e.TransformedCovenant, Equipments = [.. e.Equipments],
        IsElite = e.IsElite, IsManual = e.IsManual, Role = e.Role, Stages = e.Stages, ExpiresAt = e.ExpiresAt, IsExpired = expired,
        IsAcquired = e.IsAcquired, Origins = [.. e.Origins]
    };

    public (int Operators, int Equipment) TrackLineup(Lineup lineup)
    {
        if (!TrackMultipleLineups) ClearAllTracking();
        return ApplyLineupTargets(lineup, true);
    }

    public void ApplyTrackedLineup(Lineup lineup)
    {
        if (!IsLineupTracked(lineup.Id)) throw new InvalidOperationException("该阵容尚未关联追踪");
        ApplyLineupTargets(lineup, _trackedLineups.Single(l => l.Id == lineup.Id).Highlighted);
    }

    private (int Operators, int Equipment) ApplyLineupTargets(Lineup lineup, bool highlight)
    {
        var previousOperators = TrackedOperators.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        var previousEquipment = TrackedEquipment.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        foreach (var existing in TrackedOperators.Where(e => e.Origins.Any(o => o.LineupId == lineup.Id)).ToList())
        {
            var remaining = existing.Origins.Where(o => o.LineupId != lineup.Id).ToList();
            if (remaining.Count == 0) TrackedOperators.Remove(existing);
            else TrackedOperators[TrackedOperators.IndexOf(existing)] = MergeOrigins(existing, remaining);
        }
        _trackedLineups.RemoveAll(l => l.Id == lineup.Id);
        _trackedLineups.Add(new TrackedLineup(lineup.Id, lineup.Name, highlight));
        var phases = new[] { (Slots: lineup.Slots, Phase: "后期"), (Slots: lineup.MidSlots, Phase: "中期"),
            (Slots: lineup.EarlySlots, Phase: "前期"), (Slots: lineup.SupportSlots, Phase: "挂件") };
        foreach (var group in phases.SelectMany(p => p.Slots.Select(slot => (Slot: slot, p.Phase)))
            .Where(p => p.Slot.OperatorName.Length > 0).GroupBy(p => p.Slot.OperatorName))
        {
            var (slot, phase) = group.First();
            var role = phase == "后期" ? TrackingRole.Final : TrackingRole.Transition;
            var existing = TrackedOperators.FirstOrDefault(e => e.Name == slot.OperatorName);
            var entry = new TrackingEntry
            {
                Name = slot.OperatorName, Type = TrackingType.Operator, Role = role,
                Stages = role == TrackingRole.Final ? "" : string.Join(" · ", new[] { "前期", "中期", "挂件" }.Where(name => group.Any(p => p.Phase == name))),
                TransformedCovenant = slot.TransformedCovenant, Equipments = [.. slot.Equipments], IsElite = slot.IsElite
            };
            var origins = existing is null ? new List<TrackingOrigin>() : existing.Origins.Count > 0
                ? existing.Origins.ToList() : [TrackingOrigin.From(existing, "")];
            origins.Add(TrackingOrigin.From(entry, lineup.Id));
            var merged = MergeOrigins(entry, origins);
            if (existing is null) TrackedOperators.Add(merged);
            else TrackedOperators[TrackedOperators.IndexOf(existing)] = merged;
        }
        RefreshExpiry();
        RefreshEquipmentRequirements();
        SyncDeployPool();
        NotifySourcesChanged();
        return (TrackedOperators.Count(e => !e.IsAcquired && !previousOperators.Contains(e.Name)), TrackedEquipment.Count(e => !e.IsAcquired && !previousEquipment.Contains(e.Name)));
    }

    /// <summary>
    /// Tracks every operator of a 道中路线 in one list: 大成 operators as <see cref="TrackingRole.Final"/>,
    /// operators only used on the way as <see cref="TrackingRole.Transition"/> (with the stages that use them).
    /// An operator already tracked as 大成 stays 大成. Returns (new 大成, new 过渡) counts.
    /// </summary>
    public (int Final, int Transition) TrackRoute(PlannedRoute route, LineupEquipmentService? equipmentRules = null)
    {
        if (!TrackMultipleLineups) ClearAllTracking();
        var finalStage = route.Stages.LastOrDefault(s => s.IsFinal) ?? route.Stages.LastOrDefault();
        if (finalStage is null) return (0, 0);
        var transforms = finalStage.Operators.ToDictionary(o => o.Name, o => o.TransformedCovenant ?? "", StringComparer.Ordinal);
        int newFinal = 0, newTransition = 0;
        foreach (var op in finalStage.Operators)
        {
            var existing = TrackedOperators.FirstOrDefault(e => e.Name == op.Name);
            var entry = new TrackingEntry { Name = op.Name, Type = TrackingType.Operator, Role = TrackingRole.Final };
            // A planned transformation is tracked as its equipment (变形同构体 + the covenant's job-change gear).
            var covenant = transforms[op.Name];
            if (covenant.Length > 0 && equipmentRules?.CanTransformInto(covenant) == true)
                entry.Equipments = [LineupEquipmentService.Shapeshifter, equipmentRules.EquipmentFor(covenant)];
            else if (covenant.Length > 0)
                entry.TransformedCovenant = covenant;
            if (existing is null) newFinal++;
            MergeIndependentTarget(entry);
        }

        // Transition operators in route order (开局 first), with the stages that use them. Operators only used
        // by stages that are already behind the current terminal level are not tracked at all.
        var transitionStages = new Dictionary<string, (List<string> Stages, int ExpiresAt)>(StringComparer.Ordinal);
        for (var i = 0; i < route.Stages.Count; i++)
        {
            var stage = route.Stages[i];
            if (ReferenceEquals(stage, finalStage)) continue;
            var expiresAt = route.ExpiresAt(i);
            foreach (var op in stage.Operators.Where(o => !transforms.ContainsKey(o.Name)))
            {
                if (!transitionStages.TryGetValue(op.Name, out var info)) info = ([], -1);
                info.Stages.Add(stage.Definition.Name);
                // Kept as long as its latest stage is (0 = never expires wins).
                var at = info.ExpiresAt == 0 || expiresAt == 0 ? 0 : Math.Max(info.ExpiresAt, expiresAt);
                transitionStages[op.Name] = (info.Stages, at);
            }
        }
        var level = TerminalLevel;
        foreach (var (name, (stages, expiresAt)) in transitionStages)
        {
            var existing = TrackedOperators.FirstOrDefault(e => e.Name == name);
            var entry = new TrackingEntry
            {
                Name = name, Type = TrackingType.Operator, Role = TrackingRole.Transition,
                Stages = string.Join(" · ", stages), ExpiresAt = expiresAt
            };
            if (entry.ExpiredAt(level)) continue;
            if (existing is null) newTransition++;
            MergeIndependentTarget(entry);
        }
        RefreshEquipmentRequirements();
        SyncDeployPool();
        NotifySourcesChanged();
        return (newFinal, newTransition);
    }

    public void AddTracking(string name, TrackingType type)
    {
        var entries = type == TrackingType.Operator ? TrackedOperators : TrackedEquipment;
        var existing = entries.FirstOrDefault(e => e.Name == name);
        if (existing is null)
        {
            entries.Add(new TrackingEntry { Name = name, Type = type, IsAcquired = type == TrackingType.Operator && _deployPool?.IsOwned(name) == true });
            return;
        }
        if (type == TrackingType.Operator && existing.Origins.Count > 0 && existing.Origins.All(o => o.LineupId.Length > 0 || o.IsRoute))
        {
            var manual = Copy(existing, existing.IsExpired);
            manual.Origins = [.. existing.Origins, TrackingOrigin.From(existing, "")];
            manual.IsManual = true;
            entries[entries.IndexOf(existing)] = manual;
            existing = manual;
        }
        if (!existing.IsAcquired && (type != TrackingType.Equipment || existing.IsManual)) return;
        // Tracked again (or an automatic equipment entry tracked by hand): keep 大成 / 过渡 and the stages.
        var again = Copy(existing, existing.IsExpired);
        again.IsAcquired = type == TrackingType.Operator && _deployPool is not null
            && (existing.IsElite ? _deployPool.IsElite(name) : _deployPool.IsOwned(name));
        again.IsManual = true;
        entries[entries.IndexOf(existing)] = again;
        if (type == TrackingType.Operator) RefreshEquipmentRequirements();
    }

    public void SetOperatorAcquired(string name, bool acquired)
    {
        var entry = TrackedOperators.FirstOrDefault(e => e.Name == name);
        if (entry is null || entry.IsAcquired == acquired) return;
        ToggleAcquired(name);
    }

    public void SetDeployPool(DeployPoolState state)
    {
        _deployPool = state;
        SyncDeployPool();
    }

    public string OperatorProgress(TrackingEntry entry) => entry.IsExpired ? "已过期" : entry.IsAcquired ? "已完成"
        : entry.IsElite && _deployPool?.IsOwned(entry.Name) == true ? "待精锐" : "待获取";

    public void SyncDeployPool()
    {
        if (_deployPool is null) return;
        foreach (var entry in TrackedOperators.ToList())
            SetOperatorAcquired(entry.Name, entry.IsElite ? _deployPool.IsElite(entry.Name) : _deployPool.IsOwned(entry.Name));
        OnPropertyChanged(nameof(OperatorProgress));
    }

    public void ToggleAcquired(string name)
    {
        var entries = TrackedOperators.Any(e => e.Name == name) ? TrackedOperators : TrackedEquipment;
        var entry = entries.Single(e => e.Name == name);
        if (entry.IsAcquired)
        {
            var resumed = Copy(entry, entry.IsExpired);
            resumed.IsAcquired = entry.Type == TrackingType.Operator && _deployPool is not null
                && (entry.IsElite ? _deployPool.IsElite(name) : _deployPool.IsOwned(name));
            if (entry.Type == TrackingType.Equipment) resumed.IsManual = true;
            entries[entries.IndexOf(entry)] = resumed;
            if (entry.Type == TrackingType.Operator) RefreshEquipmentRequirements();
            return;
        }
        var acquired = Copy(entry, entry.IsExpired);
        acquired.IsAcquired = true;
        entries[entries.IndexOf(entry)] = acquired;
        if (entry.Type == TrackingType.Operator) RefreshEquipmentRequirements();
    }

    public void RemoveTracking(string name)
    {
        var opEntry = TrackedOperators.FirstOrDefault(e => e.Name == name);
        if (opEntry != null)
        {
            TrackedOperators.Remove(opEntry);
            NotifySourcesChanged();
            RefreshEquipmentRequirements();
            return;
        }

        var eqEntry = TrackedEquipment.FirstOrDefault(e => e.Name == name);
        if (eqEntry != null)
        {
            TrackedEquipment.Remove(eqEntry);
            RemoveEquipmentRequirement(name);
        }
    }

    private void RemoveEquipmentRequirement(string name)
    {
        foreach (var entry in TrackedOperators.Where(e => e.Equipments.Contains(name)).ToList())
        {
            var without = Copy(entry, entry.IsExpired);
            without.Equipments = entry.Equipments.Where(e => e != name).ToList();
            without.Origins = entry.Origins.Select(o => o with { Equipments = o.Equipments.Where(e => e != name).ToList() }).ToList();
            TrackedOperators[TrackedOperators.IndexOf(entry)] = without;
        }
    }

    public void ClearAllTracking()
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
        _trackedLineups.Clear();
        NotifySourcesChanged();
    }

    public void ClearPlannedTransformation(string name, string equipmentName, LineupEquipmentService equipmentRules)
    {
        var entry = TrackedOperators.Single(e => e.Name == name);
        var slot = new LineupSlot { OperatorName = name, Equipments = [.. entry.Equipments], IsElite = entry.IsElite };
        if (equipmentName.Length > 0)
        {
            var index = equipmentRules.Transformations(slot).FindIndex(t => t.EquipmentName == equipmentName);
            equipmentRules.SetTransformation(slot, index, "");
        }
        var cleared = Copy(entry, entry.IsExpired);
        cleared.TransformedCovenant = null;
        cleared.Equipments = slot.Equipments;
        cleared.Origins = entry.Origins.Select(o => o with { TransformedCovenant = null,
            Equipments = o.Equipments.Where(slot.Equipments.Contains).ToList() }).ToList();
        TrackedOperators[TrackedOperators.IndexOf(entry)] = cleared;
        RefreshEquipmentRequirements();
    }

    private void RefreshEquipmentRequirements()
    {
        var required = TrackedOperators.Where(e => !e.IsAcquired).SelectMany(e => e.Equipments).ToHashSet();
        foreach (var entry in TrackedEquipment.Where(e => !e.IsManual && !e.IsAcquired && !required.Contains(e.Name)).ToList())
            TrackedEquipment.Remove(entry);
        foreach (var name in required)
            if (!TrackedEquipment.Any(e => e.Name == name))
                TrackedEquipment.Add(new TrackingEntry { Name = name, Type = TrackingType.Equipment, IsManual = false });
    }

    public bool IsTracked(string name)
    {
        return TrackedOperators.Any(e => e.Name == name && !e.IsAcquired)
            || TrackedEquipment.Any(e => e.Name == name && !e.IsAcquired);
    }

    public void LoadState(TrackingState state)
    {
        LoadEntries(state.Entries);
        _trackedLineups = state.Lineups.ToList();
        NotifySourcesChanged();
    }

    public void LoadEntries(List<TrackingEntry> entries)
    {
        _trackedLineups.Clear();
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
        foreach (var entry in entries)
        {
            entry.Stages = RouteStageNames.Normalize(entry.Stages);
            if (entry.Type == TrackingType.Operator)
                TrackedOperators.Add(entry);
            else
                TrackedEquipment.Add(entry);
        }
        RefreshEquipmentRequirements();
        RefreshExpiry();
        SyncDeployPool();
        NotifySourcesChanged();
    }
}
