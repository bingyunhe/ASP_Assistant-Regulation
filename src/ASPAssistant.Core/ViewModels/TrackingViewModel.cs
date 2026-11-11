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

    public IReadOnlyList<TrackingEntry> AllEntries =>
        [.. TrackedOperators, .. TrackedEquipment];

    public (int Operators, int Equipment) TrackLineup(Lineup lineup)
    {
        if (!TrackMultipleLineups) ClearAllTracking();
        var previousOperators = TrackedOperators.Select(e => e.Name).ToHashSet();
        var previousEquipment = TrackedEquipment.Select(e => e.Name).ToHashSet();
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
                else if (existing.IsElite != slot.IsElite || !existing.Equipments.SequenceEqual(slot.Equipments))
                    TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
            }
        }
        RefreshEquipmentRequirements();
        return (TrackedOperators.Count(e => !previousOperators.Contains(e.Name)), TrackedEquipment.Count(e => !previousEquipment.Contains(e.Name)));
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
        var previous = TrackedOperators.Count;
        foreach (var lineup in batch)
            foreach (var slot in lineup.Slots)
                TrackSlotOperator(slot);
        RefreshEquipmentRequirements();
        return TrackedOperators.Count - previous;
    }

    private void TrackSlotOperator(LineupSlot slot)
    {
        if (string.IsNullOrEmpty(slot.OperatorName)) return;
        var existing = TrackedOperators.FirstOrDefault(e => e.Name == slot.OperatorName);
        var entry = new TrackingEntry
        {
            Name = slot.OperatorName, Type = TrackingType.Operator,
            Equipments = [.. slot.Equipments], IsElite = slot.IsElite
        };
        if (existing is null) TrackedOperators.Add(entry);
        else if (existing.IsElite != slot.IsElite || !existing.Equipments.SequenceEqual(slot.Equipments))
            TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
    }

    public void AddTracking(string name, TrackingType type)
    {
        if (type == TrackingType.Equipment && TrackedEquipment.FirstOrDefault(e => e.Name == name) is { IsManual: false } automatic)
        {
            TrackedEquipment[TrackedEquipment.IndexOf(automatic)] = new TrackingEntry { Name = name, Type = type };
            return;
        }
        if (IsTracked(name))
            return;

        var entry = new TrackingEntry { Name = name, Type = type };
        if (type == TrackingType.Operator)
            TrackedOperators.Add(entry);
        else
            TrackedEquipment.Add(entry);
    }

    public void RemoveTracking(string name)
    {
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
            foreach (var entry in TrackedOperators.Where(e => e.Equipments.Contains(name)).ToList())
                TrackedOperators[TrackedOperators.IndexOf(entry)] = new TrackingEntry
                {
                    Name = entry.Name, Type = TrackingType.Operator, IsElite = entry.IsElite,
                    TransformedCovenant = entry.TransformedCovenant, Equipments = entry.Equipments.Where(e => e != name).ToList()
                };
        }
    }

    public void ClearAllTracking()
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
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
            Name = name, Type = TrackingType.Operator, IsElite = entry.IsElite, Equipments = slot.Equipments
        };
        RefreshEquipmentRequirements();
    }

    private void RefreshEquipmentRequirements()
    {
        var required = TrackedOperators.SelectMany(e => e.Equipments).ToHashSet();
        foreach (var entry in TrackedEquipment.Where(e => !e.IsManual && !required.Contains(e.Name)).ToList())
            TrackedEquipment.Remove(entry);
        foreach (var name in required)
            if (!TrackedEquipment.Any(e => e.Name == name))
                TrackedEquipment.Add(new TrackingEntry { Name = name, Type = TrackingType.Equipment, IsManual = false });
    }

    public bool IsTracked(string name)
    {
        return TrackedOperators.Any(e => e.Name == name)
            || TrackedEquipment.Any(e => e.Name == name);
    }

    public void LoadEntries(List<TrackingEntry> entries)
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
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
