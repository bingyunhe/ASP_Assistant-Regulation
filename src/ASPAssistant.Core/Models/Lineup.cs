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

    /// <summary>规划器为该干员临时追加的转职盟约。</summary>
    [JsonPropertyName("transformedCovenant")]
    public string TransformedCovenant { get; set; } = "";

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
    /// 升本计划（起点）：索引 0 是 1 本（固定为开局，忽略），索引 5 是 6 本；
    /// 与 <see cref="UpgradeTo"/> 成对使用，两端都填了才算一本计划，null 表示这一本不提醒。
    /// </summary>
    [JsonPropertyName("upgradeFrom")]
    public int?[] UpgradeFrom { get; set; } = new int?[UpgradeSchedule.LevelCount];

    /// <summary>升本计划（终点）：与 <see cref="UpgradeFrom"/> 一一对应，闭区间。</summary>
    [JsonPropertyName("upgradeTo")]
    public int?[] UpgradeTo { get; set; } = new int?[UpgradeSchedule.LevelCount];

    /// <summary>是否在这个阵容被选为"当前使用"时按计划提醒升本。</summary>
    /// <summary>
    /// 本套阵容打算用的<strong>开局策略</strong>：一局只能选一个，选它同时决定初始目标生命值。
    /// 对外仍是 strategies 列表的第一项（旧文件里可能存了多个，取第一个非空的），
    /// 这样旧阵容文件和分享链接都还能读。
    /// </summary>
    [JsonIgnore]
    public string OpeningStrategyId =>
        Strategies.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.StrategyId))?.StrategyId ?? string.Empty;

    /// <summary>开局策略的名称，由界面层按 data/strategies.json 解析后回填，不落盘。</summary>
    [JsonIgnore]
    public string OpeningStrategyName { get; set; } = string.Empty;

    /// <summary>阵容卡片上显示的开局摘要，例如「开局 众志合一」；没选时为空串。</summary>
    [JsonIgnore]
    public string OpeningStrategyText =>
        OpeningStrategyName.Length == 0 ? string.Empty : $"开局 {OpeningStrategyName}";

    /// <summary>设置开局策略；传空表示不限定。</summary>
    public void SetOpeningStrategy(string? strategyId)
        => Strategies = string.IsNullOrWhiteSpace(strategyId)
            ? []
            : [new LineupStrategySlot { StrategyId = strategyId.Trim() }];

    [JsonPropertyName("upgradeReminderEnabled")]
    public bool UpgradeReminderEnabled { get; set; } = true;

    /// <summary>解析后的升本计划（非法/越界的回合值会被丢弃）。</summary>
    [JsonIgnore]
    public UpgradeSchedule UpgradePlan => UpgradeSchedule.FromArrays(UpgradeFrom, UpgradeTo);

    /// <summary>阵容卡片上显示的计划摘要，例如「2本 3–4 · 3本 5–7」；没设置时为空串。</summary>
    [JsonIgnore]
    public string UpgradePlanText => UpgradePlan.IsEmpty ? string.Empty : UpgradePlan.Describe();

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

    /// <summary>效果说明，来自手工维护的 data/covenant_effects.json（不写回 covenants.json）。</summary>
    [JsonIgnore]
    public List<CovenantEffect> Effects { get; set; } = [];
}

/// <summary>
/// 一条盟约效果。<see cref="Condition"/> 为空表示激活后的基础效果，否则是游戏内 &lt;…&gt; 里的额外条件，
/// 例如「在场6名不同的【炎】干员」「每叠加25层」。
/// </summary>
public class CovenantEffect
{
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonIgnore]
    public bool HasCondition => !string.IsNullOrWhiteSpace(Condition);
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
