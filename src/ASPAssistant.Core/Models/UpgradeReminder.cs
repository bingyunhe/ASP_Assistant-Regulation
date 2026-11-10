// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>
/// 一次「该升本了」提醒：第 <paramref name="Round"/> 回合该升到第 <paramref name="Level"/> 本。
/// 由 <see cref="Services.UpgradeReminderService"/> 产生，便于 UI 显示与测试断言。
/// </summary>
public sealed record UpgradeReminder(int Round, int Level)
{
    public string Message => $"第 {Round} 回合：升到 {Level} 本";
}
