// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

/// <summary>
/// 阵容槽位标签：玩家手动给某个干员标记，仅作分类/筛选/可视化用，不影响盟约激活计算。
/// </summary>
public enum LineupTag
{
    叠层,
    输出,
    控场,
    经济,
    阵容核心,
}

public enum LineupPhase
{
    前期,
    中期,
    后期,
    挂件,
}

public class LineupSlot
{
    [JsonPropertyName("operatorName")]
    public string OperatorName { get; set; } = "";

    /// <summary>装备名列表，最多 2 个。</summary>
    [JsonPropertyName("equipments")]
    public List<string> Equipments { get; set; } = [];

    [JsonPropertyName("coreEquipments")]
    public List<string> CoreEquipments { get; set; } = [];

    /// <summary>标签列表，最多 2 个。</summary>
    [JsonPropertyName("tags")]
    public List<LineupTag> Tags { get; set; } = [];
}

public class Lineup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>后期阵容（最多 9 个），也是列表预览和追踪使用的阵容。</summary>
    [JsonPropertyName("slots")]
    public List<LineupSlot> Slots { get; set; } = [];

    [JsonPropertyName("earlySlots")]
    public List<LineupSlot> EarlySlots { get; set; } = [];

    [JsonPropertyName("midSlots")]
    public List<LineupSlot> MidSlots { get; set; } = [];

    [JsonPropertyName("supportSlots")]
    public List<LineupSlot> SupportSlots { get; set; } = [];

    [JsonPropertyName("strategies")]
    public List<LineupStrategySlot> Strategies { get; set; } = [];

    /// <summary>
    /// 升本计划：索引 0 是「升到 1 本」的计划回合，索引 5 是「升到 6 本」；
    /// null 表示这一本不提醒。一局最多 6 本，所以固定 6 个位置。
    /// </summary>
    [JsonPropertyName("upgradeRounds")]
    public int?[] UpgradeRounds { get; set; } = new int?[UpgradeSchedule.LevelCount];

    /// <summary>是否在这个阵容被选为"当前使用"时按计划提醒升本。</summary>
    [JsonPropertyName("upgradeReminderEnabled")]
    public bool UpgradeReminderEnabled { get; set; } = true;

    /// <summary>解析后的升本计划（非法/越界的回合值会被丢弃）。</summary>
    [JsonIgnore]
    public UpgradeSchedule UpgradePlan => UpgradeSchedule.FromArray(UpgradeRounds);

    /// <summary>
    /// 是否为「当前使用」的阵容（只影响本机升本提醒，不落盘到阵容文件）。
    /// 由 SettingsManager 的 active-lineup.txt 记录，启动时按 Id 匹配回填。
    /// </summary>
    [JsonIgnore]
    public bool IsActive { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum StrategyTag
{
    阵容核心,
    优选,
    次选,
}

public class LineupStrategySlot
{
    [JsonPropertyName("strategyId")]
    public string StrategyId { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<StrategyTag> Tags { get; set; } = [];
}

/// <summary>
/// 单条盟约定义（来自 data/covenants.json，可被 covenants.user.json 覆盖）。
/// </summary>
public class CovenantInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>"SEASON" = 核心盟约（赛季限定）；"REGULAR" = 普通附加盟约。</summary>
    [JsonPropertyName("bondType")]
    public string BondType { get; set; } = "REGULAR";

    /// <summary>激活该盟约所需的同盟约干员数；null 表示数据缺失，应在 user 配置中补齐。</summary>
    [JsonPropertyName("activateCount")]
    public int? ActivateCount { get; set; }
}

/// <summary>
/// 阵容某盟约的实时统计：用于 UI 展示「已激活/层数/阈值」。
/// </summary>
public record CovenantStat(
    string Name,
    string BondType,
    int Count,
    int? ActivateCount,
    bool Activated);
