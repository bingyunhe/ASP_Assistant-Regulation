// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Xunit;

namespace ASPAssistant.Tests.Services;

public class UpgradeReminderServiceTests
{
    private static Lineup Planned(params int?[] rounds) => new()
    {
        Name = "测试阵容",
        UpgradeRounds = rounds,
    };

    [Fact]
    public void Observe_RemindsOnceOnThePlannedRound()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned(null, null, 8);

        service.Observe(8, lineup, upgradeMaxed: false).Should().Be(new UpgradeReminder(8, 3));
        service.Observe(8, lineup, upgradeMaxed: false).Should().BeNull("同一回合只提醒一次");
        service.RemindedRounds.Should().Equal(8);
    }

    [Fact]
    public void Observe_StaysSilentOnOtherRounds()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned(null, null, 8);

        service.Observe(7, lineup, false).Should().BeNull();
        service.Observe(9, lineup, false).Should().BeNull();
        service.RemindedRounds.Should().BeEmpty();
    }

    [Fact]
    public void Observe_ReturnsLowestLevelWhenSeveralShareTheRound()
    {
        var service = new UpgradeReminderService();

        service.Observe(5, Planned(null, 5, 5), false).Should().Be(new UpgradeReminder(5, 2));
    }

    [Fact]
    public void Observe_FiresAgainOnTheNextPlannedRound()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned(null, 4, null, 8, null, null);

        service.Observe(4, lineup, false).Should().Be(new UpgradeReminder(4, 2));
        service.Observe(8, lineup, false).Should().Be(new UpgradeReminder(8, 4));
        service.RemindedRounds.Should().Equal(4, 8);
    }

    [Fact]
    public void Observe_IsSilentWhenNothingPlanned()
    {
        var service = new UpgradeReminderService();

        service.Observe(9, new Lineup(), false).Should().BeNull();
        service.Observe(9, Planned(), false).Should().BeNull();
        service.Observe(9, null, false).Should().BeNull();
    }

    [Fact]
    public void Observe_IsSilentWhenReminderDisabled()
    {
        var lineup = Planned(null, 6);
        lineup.UpgradeReminderEnabled = false;

        new UpgradeReminderService().Observe(6, lineup, false).Should().BeNull();
    }

    [Fact]
    public void Observe_IsSilentWhenAlreadyMaxed()
    {
        var service = new UpgradeReminderService();

        service.Observe(8, Planned(8), upgradeMaxed: true).Should().BeNull();
        // 满级回合也要记下来，避免刚满级又补发一次。
        service.RemindedRounds.Should().Equal(new[] { 8 });
    }

    [Fact]
    public void Observe_ResetsWhenTheRoundGoesBackwards()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned(3, 6);

        service.Observe(3, lineup, false).Should().NotBeNull();
        service.Observe(9, lineup, false).Should().BeNull();

        service.Observe(2, lineup, false).Should().BeNull("第 2 回合不在计划里，但应当已经开新局");
        service.LastRound.Should().Be(2);

        service.Observe(3, lineup, false).Should().Be(new UpgradeReminder(3, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Observe_IgnoresInvalidRoundsWithoutChangingState(int round)
    {
        var service = new UpgradeReminderService();
        var lineup = Planned(4);

        service.Observe(6, lineup, false).Should().BeNull();
        service.Observe(round, lineup, false).Should().BeNull();
        service.LastRound.Should().Be(6);
    }

    [Fact]
    public void Reset_ClearsRemindedRounds()
    {
        var service = new UpgradeReminderService();
        var lineup = Planned(5);

        service.Observe(5, lineup, false).Should().NotBeNull();
        service.Reset();

        service.RemindedRounds.Should().BeEmpty();
        service.LastRound.Should().Be(0);
        service.Observe(5, lineup, false).Should().NotBeNull();
    }

    [Fact]
    public void IsDueAt_MatchesTheReminderRule()
    {
        var lineup = Planned(null, null, 8);

        UpgradeReminderService.IsDueAt(7, lineup).Should().BeFalse();
        UpgradeReminderService.IsDueAt(8, lineup).Should().BeTrue();
        UpgradeReminderService.IsDueAt(9, lineup).Should().BeFalse();
        UpgradeReminderService.IsDueAt(0, lineup).Should().BeFalse();
        UpgradeReminderService.IsDueAt(8, null).Should().BeFalse();

        var disabled = Planned(null, null, 8);
        disabled.UpgradeReminderEnabled = false;
        UpgradeReminderService.IsDueAt(8, disabled).Should().BeFalse();
    }

    [Fact]
    public void ReminderMessage_NamesTheRoundAndLevel()
    {
        new UpgradeReminder(9, 3).Message.Should().Be("第 9 回合：升到 3 本");
    }
}
