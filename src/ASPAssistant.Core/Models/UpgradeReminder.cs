// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>
/// 一次「该升本了」提醒：第 <paramref name="Round"/> 回合，该升到第 <paramref name="Level"/> 本，
/// 这一本的计划区间是 <paramref name="Window"/>（用来告诉玩家还来不来得及）。
/// </summary>
public sealed record UpgradeReminder(int Round, int Level, RoundWindow Window)
{
    /// <summary>区间里还剩几个回合（含当前回合）。</summary>
    public int RoundsLeft => Math.Max(0, Window.To - Round + 1);

    public string Message => Round >= Window.To
        ? $"第 {Round} 回合：升 {Level} 本（计划 {Window.Short}，这是最后一回合）"
        : $"第 {Round} 回合：升 {Level} 本（计划 {Window.Short}，还剩 {RoundsLeft} 回合）";
}
