// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// 「升本回合提醒」的全部判定逻辑，与 UI 解耦以便单测。
/// 口径：每本有自己的计划回合，<strong>到达那个回合的下一帧提醒一次</strong>。
///
/// 规则：
/// <list type="bullet">
/// <item>回合数无效（&lt;= 0，即这一帧没读到回合）不提醒，也不改变状态。</item>
/// <item>同一回合只提醒一次——扫描每 1.5 秒跑一次，靠回合号去重而不是靠计时器。</item>
/// <item>回合数变小说明开了新一局，清空去重记录。</item>
/// <item>游戏里已经显示「已满级」时不再提醒。</item>
/// <item>阵容未开启提醒、或 6 本都没设回合时不提醒。</item>
/// </list>
/// </summary>
public sealed class UpgradeReminderService
{
    private readonly HashSet<int> _remindedRounds = [];
    private int _lastRound;

    /// <summary>已提醒过的回合（调试/测试用只读快照）。</summary>
    public IReadOnlyCollection<int> RemindedRounds => _remindedRounds;

    /// <summary>上一次看到的有效回合；0 表示还没读到过。</summary>
    public int LastRound => _lastRound;

    /// <summary>
    /// 用一帧的识别结果推进状态机。需要提醒时返回提醒内容，否则返回 null。
    /// 同一回合若命中多本，只返回本数最低的那一本（计划的下一本）。
    /// </summary>
    public UpgradeReminder? Observe(int round, Lineup? lineup, bool upgradeMaxed)
    {
        if (round <= 0) return null;

        if (round < _lastRound) Reset(); // 新的一局
        _lastRound = round;

        if (upgradeMaxed)
        {
            // 已经满级，本回合不需要再提示；记下来避免同回合重复判定。
            _remindedRounds.Add(round);
            return null;
        }

        if (!TryGetSchedule(lineup, out var schedule)) return null;

        var levels = schedule.LevelsAt(round);
        if (levels.Count == 0) return null;
        if (!_remindedRounds.Add(round)) return null;
        return new UpgradeReminder(round, levels[0]);
    }

    /// <summary>该阵容是否计划在这个回合升本（不看去重状态）。</summary>
    public static bool IsDueAt(int round, Lineup? lineup)
        => round > 0 && TryGetSchedule(lineup, out var schedule) && schedule.LevelsAt(round).Count > 0;

    /// <summary>清空去重记录（切换/重新追踪阵容、手动关闭提醒时调用）。</summary>
    public void Reset()
    {
        _remindedRounds.Clear();
        _lastRound = 0;
    }

    private static bool TryGetSchedule(Lineup? lineup, out UpgradeSchedule schedule)
    {
        schedule = UpgradeSchedule.Empty;
        if (lineup is null || !lineup.UpgradeReminderEnabled) return false;

        var plan = lineup.UpgradePlan;
        if (plan.IsEmpty) return false;
        schedule = plan;
        return true;
    }
}
