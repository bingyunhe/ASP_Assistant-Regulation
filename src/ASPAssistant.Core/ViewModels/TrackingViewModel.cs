// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

public partial class TrackingViewModel : ObservableObject
{
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

    /// <summary>The saved lineup currently feeding the tracking list, shown by the phase switch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSourceLineup), nameof(SourceLineupName), nameof(SourcePhaseOperatorCount))]
    private Lineup? _sourceLineup;

    /// <summary>Which phase of <see cref="SourceLineup"/> is currently tracked; null = all four phases merged.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourcePhaseOperatorCount))]
    private LineupPhase? _sourcePhase;

    /// <summary>Names this lineup added to the tracking list; switching phases swaps only these.</summary>
    private readonly HashSet<string> _sourceTrackedNames = new(StringComparer.Ordinal);

    public bool HasSourceLineup => SourceLineup is not null;

    public string SourceLineupName => SourceLineup is null ? ""
        : string.IsNullOrWhiteSpace(SourceLineup.Name) ? "未命名阵容" : SourceLineup.Name;

    public int SourcePhaseOperatorCount => SourceLineup is null
        ? 0
        : PhaseSlots(SourceLineup, SourcePhase).Count(s => !string.IsNullOrEmpty(s.OperatorName));

    public IReadOnlyList<TrackingEntry> AllEntries =>
        [.. TrackedOperators, .. TrackedEquipment];

    public (int Operators, int Equipment) TrackLineup(Lineup lineup)
    {
        if (!TrackMultipleLineups) ClearAllTracking();
        else ClearSourceLineup();
        var previousOperators = TrackedOperators.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        var previousEquipment = TrackedEquipment.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        foreach (var slot in lineup.Slots.Concat(lineup.MidSlots).Concat(lineup.EarlySlots).Concat(lineup.SupportSlots)
            .DistinctBy(slot => slot.OperatorName))
        {
            if (!string.IsNullOrEmpty(slot.OperatorName))
            {
                var existing = TrackedOperators.FirstOrDefault(e => e.Name == slot.OperatorName);
                var entry = new TrackingEntry
                {
                    Name = slot.OperatorName, Type = TrackingType.Operator,
                    Equipments = [.. slot.Equipments], IsElite = slot.IsElite
                };
                if (existing is null) TrackedOperators.Add(entry);
                else if (existing.IsAcquired || existing.IsElite != slot.IsElite
                    || !existing.Equipments.SequenceEqual(slot.Equipments))
                    TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
            }
        }
        RefreshEquipmentRequirements();
        return (TrackedOperators.Count(e => !e.IsAcquired && !previousOperators.Contains(e.Name)), TrackedEquipment.Count(e => !e.IsAcquired && !previousEquipment.Contains(e.Name)));
    }

    /// <summary>
    /// Tracks one saved lineup and remembers it as the source of the tracking page's phase switch.
    /// The default view merges all four phases; re-tracking the same lineup keeps the phase the
    /// player already chose.
    /// </summary>
    public (int Operators, int Equipment) TrackSavedLineup(Lineup lineup, LineupPhase? phase = null)
    {
        var sameLineup = SourceLineup?.Id == lineup.Id;
        var targetPhase = phase ?? (sameLineup ? SourcePhase : null);
        if (!TrackMultipleLineups)
        {
            ClearTrackingEntries();
            _sourceTrackedNames.Clear();
        }
        else if (!sameLineup)
        {
            _sourceTrackedNames.Clear();
        }

        SourceLineup = lineup;
        SourcePhase = targetPhase;
        var previousOperators = TrackedOperators.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        var previousEquipment = TrackedEquipment.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        ApplySourcePhase(removeOwned: sameLineup);
        return (TrackedOperators.Count(e => !e.IsAcquired && !previousOperators.Contains(e.Name)),
            TrackedEquipment.Count(e => !e.IsAcquired && !previousEquipment.Contains(e.Name)));
    }

    /// <summary>Switches the tracked phase of the remembered saved lineup, swapping only its entries.</summary>
    public (int Operators, int Equipment) SelectTrackedLineupPhase(LineupPhase phase)
    {
        if (SourceLineup is null) return (0, 0);

        if (!TrackMultipleLineups)
        {
            ClearTrackingEntries();
            _sourceTrackedNames.Clear();
        }
        var previousOperators = TrackedOperators.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        var previousEquipment = TrackedEquipment.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        // Clicking the active phase again returns to the merged four-phase view.
        SourcePhase = SourcePhase == phase ? null : phase;
        ApplySourcePhase(removeOwned: true);
        return (TrackedOperators.Count(e => !e.IsAcquired && !previousOperators.Contains(e.Name)),
            TrackedEquipment.Count(e => !e.IsAcquired && !previousEquipment.Contains(e.Name)));
    }

    /// <summary>Returns the remembered lineup to the default four-phase merged view.</summary>
    public void SelectAllTrackedPhases()
    {
        if (SourceLineup is null) return;
        SourcePhase = null;
        ApplySourcePhase(removeOwned: true);
    }

    /// <summary>
    /// Adds every late-phase operator of the given lineups in one batch, merging duplicates across teams.
    /// Equipment rows are derived from the operators' equipment configuration, matching v1.6.0 tracking.
    /// Returns the number of operators added to the tracking list.
    /// </summary>
    public int TrackLineupsOperators(IEnumerable<Lineup> lineups)
    {
        var batch = lineups as IReadOnlyCollection<Lineup> ?? lineups.ToList();
        if (!TrackMultipleLineups) ClearAllTracking();
        else ClearSourceLineup();
        var previous = TrackedOperators.Count;
        foreach (var lineup in batch)
            foreach (var slot in lineup.Slots)
                TrackSlotOperator(slot);
        RefreshEquipmentRequirements();
        return TrackedOperators.Count - previous;
    }

    /// <summary>Adds the slot's operator, returns true only when it was newly added.</summary>
    private bool TrackSlotOperator(LineupSlot slot, bool preserveAcquired = false)
    {
        if (string.IsNullOrEmpty(slot.OperatorName)) return false;
        var existing = TrackedOperators.FirstOrDefault(e => e.Name == slot.OperatorName);
        var entry = new TrackingEntry
        {
            Name = slot.OperatorName, Type = TrackingType.Operator,
            Equipments = [.. slot.Equipments], IsElite = slot.IsElite
        };
        if (existing is null)
        {
            TrackedOperators.Add(entry);
            return true;
        }
        if (existing.IsAcquired && !preserveAcquired)
        {
            TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
            return false;
        }
        if (existing.IsElite != slot.IsElite || !existing.Equipments.SequenceEqual(slot.Equipments))
        {
            if (preserveAcquired && existing.IsAcquired) entry.IsAcquired = true;
            TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
        }
        return false;
    }

    private void ApplySourcePhase(bool removeOwned)
    {
        if (SourceLineup is not { } lineup) return;
        var slots = PhaseSlots(lineup, SourcePhase);
        var phaseNames = slots
            .Where(slot => !string.IsNullOrEmpty(slot.OperatorName))
            .Select(slot => slot.OperatorName)
            .ToHashSet(StringComparer.Ordinal);

        if (removeOwned)
        {
            // Only remove operators this lineup contributed. Equipment is derived from the
            // remaining operators by RefreshEquipmentRequirements; removing an equipment entry
            // directly would also strip it from the operator's saved loadout.
            foreach (var name in _sourceTrackedNames.Where(n => !phaseNames.Contains(n)).ToList())
            {
                var entry = TrackedOperators.FirstOrDefault(e => e.Name == name);
                if (entry is not null && !entry.IsAcquired) TrackedOperators.Remove(entry);
                _sourceTrackedNames.Remove(name);
            }
        }

        foreach (var slot in slots)
            if (TrackSlotOperator(slot, preserveAcquired: true)) _sourceTrackedNames.Add(slot.OperatorName);

        RefreshEquipmentRequirements();
    }

    private static IReadOnlyList<LineupSlot> PhaseSlots(Lineup lineup, LineupPhase? phase) => phase switch
    {
        LineupPhase.前期 => lineup.EarlySlots,
        LineupPhase.中期 => lineup.MidSlots,
        LineupPhase.挂件 => lineup.SupportSlots,
        LineupPhase.后期 => lineup.Slots,
        _ => lineup.Slots.Concat(lineup.MidSlots).Concat(lineup.EarlySlots).Concat(lineup.SupportSlots)
            .Where(slot => !string.IsNullOrEmpty(slot.OperatorName))
            .DistinctBy(slot => slot.OperatorName)
            .ToList(),
    };

    public void AddTracking(string name, TrackingType type)
    {
        var entries = type == TrackingType.Operator ? TrackedOperators : TrackedEquipment;
        var existing = entries.FirstOrDefault(e => e.Name == name);
        if (existing is null)
        {
            entries.Add(new TrackingEntry { Name = name, Type = type });
            return;
        }
        if (!existing.IsAcquired && (type != TrackingType.Equipment || existing.IsManual)) return;
        entries[entries.IndexOf(existing)] = new TrackingEntry
        {
            Name = name, Type = type, TransformedCovenant = existing.TransformedCovenant,
            Equipments = [.. existing.Equipments], IsElite = existing.IsElite
        };
        if (type == TrackingType.Operator) RefreshEquipmentRequirements();
    }

    public void ToggleAcquired(string name)
    {
        var entries = TrackedOperators.Any(e => e.Name == name) ? TrackedOperators : TrackedEquipment;
        var entry = entries.Single(e => e.Name == name);
        if (entry.IsAcquired)
        {
            AddTracking(name, entry.Type);
            return;
        }
        entries[entries.IndexOf(entry)] = new TrackingEntry
        {
            Name = entry.Name, Type = entry.Type, IsAcquired = true, IsManual = entry.IsManual,
            TransformedCovenant = entry.TransformedCovenant, Equipments = [.. entry.Equipments], IsElite = entry.IsElite
        };
        if (entry.Type == TrackingType.Operator)
        {
            RefreshEquipmentRequirements();
            return;
        }
        RemoveEquipmentRequirement(name);
    }

    public void RemoveTracking(string name)
    {
        _sourceTrackedNames.Remove(name);
        var opEntry = TrackedOperators.FirstOrDefault(e => e.Name == name);
        if (opEntry != null)
        {
            TrackedOperators.Remove(opEntry);
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
            TrackedOperators[TrackedOperators.IndexOf(entry)] = new TrackingEntry
            {
                Name = entry.Name, Type = TrackingType.Operator, IsElite = entry.IsElite,
                IsAcquired = entry.IsAcquired, TransformedCovenant = entry.TransformedCovenant,
                Equipments = entry.Equipments.Where(e => e != name).ToList()
            };
    }

    public void ClearAllTracking()
    {
        ClearTrackingEntries();
        ClearSourceLineup();
    }

    private void ClearTrackingEntries()
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
    }

    private void ClearSourceLineup()
    {
        SourceLineup = null;
        SourcePhase = null;
        _sourceTrackedNames.Clear();
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
        TrackedOperators[TrackedOperators.IndexOf(entry)] = new TrackingEntry
        {
            Name = name, Type = TrackingType.Operator, IsElite = entry.IsElite, Equipments = slot.Equipments,
            IsAcquired = entry.IsAcquired
        };
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

    public void LoadEntries(List<TrackingEntry> entries)
    {
        ClearTrackingEntries();
        ClearSourceLineup();
        foreach (var entry in entries)
        {
            if (entry.Type == TrackingType.Operator)
                TrackedOperators.Add(entry);
            else
                TrackedEquipment.Add(entry);
        }
        RefreshEquipmentRequirements();
    }
}
