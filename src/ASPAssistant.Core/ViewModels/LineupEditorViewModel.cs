// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

/// <summary>
/// 阵容编辑会话的临时 view-model：固定持有 9 个槽位，未填的槽位 OperatorName 为空。
/// 所有修改后调用 <see cref="Recalculate"/> 刷新激活盟约统计。
/// </summary>
public partial class LineupEditorViewModel : ObservableObject
{
    public const int MaxSlots = 9;
    public const int MaxEquipPerSlot = 2;
    public const int MaxTagsPerSlot = 2;

    private readonly CovenantCalculator _calculator;
    private readonly string _initialName;
    private readonly Dictionary<LineupPhase, List<LineupSlot>> _initialSlots;
    private readonly Dictionary<LineupPhase, ObservableCollection<LineupSlot>> _phaseSlots = [];
    private readonly LineupPhase _initialPhase;
    private readonly string _initialOpeningStrategyId;
    private readonly bool _initialUpgradeReminderEnabled;
    private readonly int?[] _initialUpgradeFrom = new int?[UpgradeSchedule.LevelCount];
    private readonly int?[] _initialUpgradeTo = new int?[UpgradeSchedule.LevelCount];

    public LineupPhase CurrentPhase { get; private set; }

    public IReadOnlyList<Operator> AllOperators { get; }
    public IReadOnlyList<Equipment> AllEquipment { get; }

    public string LineupId { get; }

    [ObservableProperty]
    private string _name = "新阵容";

    /// <summary>9 个槽位，空槽位的 OperatorName 为空字符串。</summary>
    [ObservableProperty]
    private ObservableCollection<LineupSlot> _slots = [];

    /// <summary>仅展示当前阵容内出现过的盟约（Count &gt; 0）。</summary>
    [ObservableProperty]
    private ObservableCollection<CovenantStat> _activeStats = [];

    [ObservableProperty]
    private int _activatedCount;

    [ObservableProperty]
    private int _filledSlotCount;

    /// <summary>开局策略 ID（一局一个，空表示不限定）。</summary>
    [ObservableProperty]
    private string _openingStrategyId = string.Empty;

    /// <summary>是否开启升本回合提醒（只在阵容被追踪时生效）。</summary>
    [ObservableProperty]
    private bool _upgradeReminderEnabled = true;

    // 2–6 本各一个起止区间；1 本是开局，不需要设置。
    [ObservableProperty]
    private int? _upgradeFrom2;

    [ObservableProperty]
    private int? _upgradeTo2;

    [ObservableProperty]
    private int? _upgradeFrom3;

    [ObservableProperty]
    private int? _upgradeTo3;

    [ObservableProperty]
    private int? _upgradeFrom4;

    [ObservableProperty]
    private int? _upgradeTo4;

    [ObservableProperty]
    private int? _upgradeFrom5;

    [ObservableProperty]
    private int? _upgradeTo5;

    [ObservableProperty]
    private int? _upgradeFrom6;

    [ObservableProperty]
    private int? _upgradeTo6;

    public LineupEditorViewModel(
        Lineup? source,
        IEnumerable<Operator> operators,
        IEnumerable<Equipment> equipment,
        CovenantCalculator calculator)
    {
        _calculator = calculator;
        AllOperators = [.. operators];
        AllEquipment = [.. equipment];

        LineupId = source?.Id ?? Guid.NewGuid().ToString("N");
        if (!string.IsNullOrWhiteSpace(source?.Name))
            _name = source!.Name;

        if (source is not null)
        {
            _upgradeReminderEnabled = source.UpgradeReminderEnabled;
            var from = source.UpgradeFrom ?? [];
            var to = source.UpgradeTo ?? [];
            _upgradeFrom2 = ReadAt(from, 1);
            _upgradeTo2 = ReadAt(to, 1);
            _upgradeFrom3 = ReadAt(from, 2);
            _upgradeTo3 = ReadAt(to, 2);
            _upgradeFrom4 = ReadAt(from, 3);
            _upgradeTo4 = ReadAt(to, 3);
            _upgradeFrom5 = ReadAt(from, 4);
            _upgradeTo5 = ReadAt(to, 4);
            _upgradeFrom6 = ReadAt(from, 5);
            _upgradeTo6 = ReadAt(to, 5);
        }
        _initialUpgradeReminderEnabled = _upgradeReminderEnabled;
        Array.Copy(UpgradeFromArray, _initialUpgradeFrom, UpgradeSchedule.LevelCount);
        Array.Copy(UpgradeToArray, _initialUpgradeTo, UpgradeSchedule.LevelCount);

        foreach (var phase in Enum.GetValues<LineupPhase>())
        {
            IEnumerable<LineupSlot> sourceSlots = source is null ? [] : phase switch
            {
                LineupPhase.前期 => source.EarlySlots,
                LineupPhase.中期 => source.MidSlots,
                LineupPhase.后期 => source.Slots,
                LineupPhase.挂件 => source.SupportSlots,
                _ => throw new ArgumentOutOfRangeException(nameof(phase)),
            };
            var slots = new ObservableCollection<LineupSlot>(sourceSlots.Take(MaxSlots).Select(CloneSlot));
            while (slots.Count < MaxSlots)
                slots.Add(new LineupSlot());
            _phaseSlots.Add(phase, slots);
        }
        _initialName = Name;
        _openingStrategyId = source?.OpeningStrategyId ?? string.Empty;
        _initialOpeningStrategyId = _openingStrategyId;
        _initialSlots = _phaseSlots.ToDictionary(pair => pair.Key, pair => pair.Value.Select(CloneSlot).ToList());
        _initialPhase = source is null ? LineupPhase.前期 : LineupPhase.后期;
        SelectPhase(_initialPhase);
    }

    public void SelectPhase(LineupPhase phase)
    {
        CurrentPhase = phase;
        Slots = _phaseSlots[phase];
        Recalculate();
    }

    /// <summary>设置某个槽位的干员（空字符串 = 清空槽位，包括其装备/标签）。</summary>
    public void SetSlotOperator(int slotIndex, string operatorName)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count) return;

        if (string.IsNullOrEmpty(operatorName))
        {
            Slots[slotIndex] = new LineupSlot();
        }
        else
        {
            var slot = Slots[slotIndex];
            slot.OperatorName = operatorName;
            // 触发 ItemsControl 刷新（LineupSlot 是 POCO，集合变更通知更可靠）
            Slots[slotIndex] = CloneSlot(slot);
        }
        Recalculate();
    }

    /// <summary>切换装备：若已装备则移除；若未装备则在不超过上限时追加。</summary>
    public void ToggleSlotEquipment(int slotIndex, string equipName)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count) return;
        if (string.IsNullOrEmpty(equipName)) return;
        var slot = CloneSlot(Slots[slotIndex]);
        if (string.IsNullOrEmpty(slot.OperatorName)) return;

        if (slot.Equipments.Contains(equipName))
        {
            slot.Equipments.Remove(equipName);
        }
        else
        {
            if (slot.Equipments.Count >= MaxEquipPerSlot)
                slot.Equipments.RemoveAt(0); // 顶替最早的
            slot.Equipments.Add(equipName);
        }
        slot.CoreEquipments.RemoveAll(name => !slot.Equipments.Contains(name));
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void SetSlotEquipment(int slotIndex, int equipmentIndex, string equipmentName, bool isCore)
    {
        var slot = CloneSlot(Slots[slotIndex]);
        if (equipmentIndex < slot.Equipments.Count)
            slot.Equipments.RemoveAt(equipmentIndex);
        if (equipmentName.Length > 0)
        {
            slot.Equipments.Remove(equipmentName);
            slot.Equipments.Insert(Math.Min(equipmentIndex, slot.Equipments.Count), equipmentName);
        }
        slot.CoreEquipments.RemoveAll(name => !slot.Equipments.Contains(name) || name == equipmentName);
        if (equipmentName.Length > 0 && isCore)
            slot.CoreEquipments.Add(equipmentName);
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void Reset()
    {
        Name = _initialName;
        UpgradeReminderEnabled = _initialUpgradeReminderEnabled;
        UpgradeFrom2 = _initialUpgradeFrom[1];
        UpgradeTo2 = _initialUpgradeTo[1];
        UpgradeFrom3 = _initialUpgradeFrom[2];
        UpgradeTo3 = _initialUpgradeTo[2];
        UpgradeFrom4 = _initialUpgradeFrom[3];
        UpgradeTo4 = _initialUpgradeTo[3];
        UpgradeFrom5 = _initialUpgradeFrom[4];
        UpgradeTo5 = _initialUpgradeTo[4];
        UpgradeFrom6 = _initialUpgradeFrom[5];
        UpgradeTo6 = _initialUpgradeTo[5];
        OpeningStrategyId = _initialOpeningStrategyId;
        foreach (var (phase, slots) in _initialSlots)
            _phaseSlots[phase] = new ObservableCollection<LineupSlot>(slots.Select(CloneSlot));
        SelectPhase(_initialPhase);
    }

    public void Clear()
    {
        Slots.Clear();
        for (int i = 0; i < MaxSlots; i++)
            Slots.Add(new LineupSlot());
        Recalculate();
    }

    public void ToggleSlotTag(int slotIndex, LineupTag tag)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count) return;
        var slot = CloneSlot(Slots[slotIndex]);
        if (string.IsNullOrEmpty(slot.OperatorName)) return;

        if (slot.Tags.Contains(tag))
        {
            slot.Tags.Remove(tag);
        }
        else
        {
            if (slot.Tags.Count >= MaxTagsPerSlot)
                slot.Tags.RemoveAt(0);
            slot.Tags.Add(tag);
        }
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void Recalculate()
    {
        var allStats = _calculator.Compute(new Lineup { Slots = [.. Slots] });
        var visible = allStats.Where(s => s.Count > 0)
                              .OrderByDescending(s => s.Activated)
                              .ThenBy(s => s.BondType == "SEASON" ? 0 : 1)
                              .ThenByDescending(s => s.Count)
                              .ThenBy(s => s.Name)
                              .ToList();
        ActiveStats = new ObservableCollection<CovenantStat>(visible);
        ActivatedCount = allStats.Count(s => s.Activated);
        FilledSlotCount = Slots.Count(s => !string.IsNullOrEmpty(s.OperatorName));
    }

    /// <summary>六个输入框当前的值，按「1 本 … 6 本」排列。</summary>
    /// <summary>2–6 本的起点，索引按本数排列（索引 0/1 是 1/2 本，1 本恒为 null）。</summary>
    public int?[] UpgradeFromArray =>
        [null, UpgradeFrom2, UpgradeFrom3, UpgradeFrom4, UpgradeFrom5, UpgradeFrom6];

    /// <summary>2–6 本的终点，与 <see cref="UpgradeFromArray"/> 一一对应。</summary>
    public int?[] UpgradeToArray =>
        [null, UpgradeTo2, UpgradeTo3, UpgradeTo4, UpgradeTo5, UpgradeTo6];

    private static int? ReadAt(int?[] values, int index)
        => index >= 0 && index < values.Length ? values[index] : null;

    public Lineup ToLineup()
    {
        return new Lineup
        {
            Id = LineupId,
            Name = string.IsNullOrWhiteSpace(Name) ? "未命名阵容" : Name.Trim(),
            Slots = ExportPhase(LineupPhase.后期),
            EarlySlots = ExportPhase(LineupPhase.前期),
            MidSlots = ExportPhase(LineupPhase.中期),
            SupportSlots = ExportPhase(LineupPhase.挂件),
            Strategies = string.IsNullOrWhiteSpace(OpeningStrategyId)
                ? []
                : [new LineupStrategySlot { StrategyId = OpeningStrategyId }],
            UpgradeReminderEnabled = UpgradeReminderEnabled,
            UpgradeFrom = UpgradeSchedule.FromArrays(UpgradeFromArray, UpgradeToArray).ToFromArray(),
            UpgradeTo = UpgradeSchedule.FromArrays(UpgradeFromArray, UpgradeToArray).ToToArray(),
            UpdatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>当前输入框对应的合法计划（越界值被丢弃）。</summary>
    public UpgradeSchedule NormalizedSchedule => UpgradeSchedule.FromArrays(UpgradeFromArray, UpgradeToArray);

    private List<LineupSlot> ExportPhase(LineupPhase phase) => [.. _phaseSlots[phase]
        .Where(s => !string.IsNullOrEmpty(s.OperatorName)).Select(CloneSlot)];

    private static LineupSlot CloneSlot(LineupSlot s) => new()
    {
        OperatorName = s.OperatorName,
        TransformedCovenant = s.TransformedCovenant,
        Equipments   = [.. s.Equipments],
        CoreEquipments = [.. s.CoreEquipments],
        Tags         = [.. s.Tags],
    };
}
