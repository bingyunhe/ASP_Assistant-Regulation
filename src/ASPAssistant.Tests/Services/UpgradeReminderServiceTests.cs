// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Xunit;

namespace ASPAssistant.Tests.Services;

public class UpgradeReminderServiceTests
{
    /// <summary>按「2–6 本各一个区间」造阵容；数组索引 0 是 1 本，恒为 null。</summary>
    private static Lineup Planned(int?[] from, int?[] to) => new()
    {
        Name = "测试阵容",
        UpgradeFrom = from,
        UpgradeTo = to,
    };

    [Fact]
    public void Observe_RemindsEveryRoundInsideTheWindow()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned([null, null, 5], [null, null, 7]);

        service.Observe(5, lineup, false).Should().Be(new UpgradeReminder(5, 3, new RoundWindow(5, 7)));
        service.Observe(6, lineup, false).Should().Be(new UpgradeReminder(6, 3, new RoundWindow(5, 7)));
        service.Observe(7, lineup, false).Should().Be(new UpgradeReminder(7, 3, new RoundWindow(5, 7)));
    }

    [Fact]
    public void Observe_RemindsOnlyOncePerRound()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned([null, null, 5], [null, null, 7]);

        service.Observe(5, lineup, false).Should().NotBeNull();
        service.Observe(5, lineup, false).Should().BeNull("同一回合只提醒一次");
        service.RemindedRounds.Should().Equal(5);
    }

    [Fact]
    public void Observe_StaysSilentOutsideTheWindow()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned([null, null, 5], [null, null, 7]);

        service.Observe(4, lineup, false).Should().BeNull();
        service.Observe(8, lineup, false).Should().BeNull();
        service.RemindedRounds.Should().BeEmpty();
    }

    [Fact]
    public void Observe_ReturnsLowestLevelWhenWindowsOverlap()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned([null, 4, 4], [null, 6, 8]);

        var reminder = service.Observe(5, lineup, false);

        reminder.Should().NotBeNull();
        reminder!.Level.Should().Be(2);
        reminder.Window.Should().Be(new RoundWindow(4, 6));
    }

    [Fact]
    public void Observe_StaysSilentWhenTheGameIsAlreadyMaxed()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned([null, null, 5], [null, null, 9]);

        service.Observe(5, lineup, upgradeMaxed: true).Should().BeNull();
        service.Observe(6, lineup, upgradeMaxed: false).Should().NotBeNull();
    }

    [Fact]
    public void Observe_StaysSilentWithoutUsablePlan()
    {
        var service = new UpgradeReminderService();

        service.Observe(5, null, false).Should().BeNull();
        service.Observe(5, new Lineup(), false).Should().BeNull();
        service.Observe(5, Planned([null, null, 5], [null, null, 9]), false).Should().NotBeNull();
    }

    [Fact]
    public void Observe_StaysSilentWhenRemindersAreDisabled()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned([null, null, 5], [null, null, 9]);
        lineup.UpgradeReminderEnabled = false;

        service.Observe(5, lineup, false).Should().BeNull();
    }

    [Fact]
    public void Observe_StartsOverWhenTheRoundCountGoesBackwards()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned([null, null, 5], [null, null, 9]);

        service.Observe(7, lineup, false).Should().NotBeNull();
        service.Observe(7, lineup, false).Should().BeNull();
        service.Observe(3, lineup, false).Should().BeNull("新一局还没到区间");

        service.RemindedRounds.Should().BeEmpty();
        service.Observe(5, lineup, false).Should().NotBeNull("新一局重新提醒");
    }

    [Fact]
    public void Observe_IgnoresUnreadRounds()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned([null, null, 5], [null, null, 9]);

        service.Observe(0, lineup, false).Should().BeNull();
        service.Observe(-1, lineup, false).Should().BeNull();
        service.LastRound.Should().Be(0);
        service.RemindedRounds.Should().BeEmpty();
    }

    [Fact]
    public void IsDueAt_MatchesTheWindowsWithoutReminderState()
    {
        var lineup = Planned([null, 4, 8], [null, 6, 10]);

        UpgradeReminderService.IsDueAt(5, lineup).Should().BeTrue();
        UpgradeReminderService.IsDueAt(9, lineup).Should().BeTrue();
        UpgradeReminderService.IsDueAt(7, lineup).Should().BeFalse();
        UpgradeReminderService.IsDueAt(0, lineup).Should().BeFalse();
        UpgradeReminderService.IsDueAt(5, null).Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsRememberedRounds()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned([null, null, 5], [null, null, 9]);

        service.Observe(5, lineup, false).Should().NotBeNull();
        service.Reset();

        service.RemindedRounds.Should().BeEmpty();
        service.LastRound.Should().Be(0);
        service.Observe(5, lineup, false).Should().NotBeNull();
    }

    [Fact]
    public void Message_SaysHowManyRoundsAreLeft()
    {
        new UpgradeReminder(4, 2, new RoundWindow(3, 6)).Message
            .Should().Be("第 4 回合：升 2 本（计划 3–6，还剩 3 回合）");

        new UpgradeReminder(6, 2, new RoundWindow(3, 6)).Message
            .Should().Contain("这是最后一回合");

        new UpgradeReminder(6, 2, new RoundWindow(3, 6)).RoundsLeft.Should().Be(1);
        new UpgradeReminder(9, 2, new RoundWindow(3, 6)).RoundsLeft.Should().Be(0);
    }
}
