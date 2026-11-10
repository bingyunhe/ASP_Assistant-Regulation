// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using System.Text.Json;
using ASPAssistant.Core.Models;
using FluentAssertions;
using Xunit;

namespace ASPAssistant.Tests.Models;

/// <summary>「升本计划」：2–6 本各一个回合区间。</summary>
public sealed class UpgradeScheduleTests
{
    private static readonly UpgradeSchedule Ordered = UpgradeSchedule.FromArrays(
        [null, 3, 5, 8, 12, 16],
        [null, 4, 7, 10, 14, 20]);

    [Fact]
    public void FromArrays_BuildsOneWindowPerLevel()
    {
        Ordered.WindowForLevel(2).Should().Be(new RoundWindow(3, 4));
        Ordered.WindowForLevel(3).Should().Be(new RoundWindow(5, 7));
        Ordered.WindowForLevel(6).Should().Be(new RoundWindow(16, 20));
        Ordered.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void FromArrays_LevelOneIsTheOpeningLevel_SoInputIsIgnored()
    {
        var schedule = UpgradeSchedule.FromArrays([2], [3]);

        schedule.WindowForLevel(1).Should().BeNull();
        schedule.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void FromArrays_NeedsBothEnds()
    {
        var schedule = UpgradeSchedule.FromArrays([null, 5, null], [null, null, 9]);

        schedule.WindowForLevel(2).Should().BeNull();
        schedule.WindowForLevel(3).Should().BeNull();
        schedule.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void FromArrays_SwapsReversedBounds()
    {
        var schedule = UpgradeSchedule.FromArrays([null, 9], [null, 6]);

        schedule.WindowForLevel(2).Should().Be(new RoundWindow(6, 9));
        schedule.Describe().Should().Be("2本 6–9");
    }

    [Fact]
    public void FromArrays_DropsOutOfRangeRounds()
    {
        var schedule = UpgradeSchedule.FromArrays([null, 0, 31, 4], [null, 5, 31, 6]);

        schedule.WindowForLevel(2).Should().BeNull();
        schedule.WindowForLevel(3).Should().BeNull();
        schedule.WindowForLevel(4).Should().Be(new RoundWindow(4, 6));
    }

    [Fact]
    public void FromArrays_ToleratesShortAndLongArrays()
    {
        UpgradeSchedule.FromArrays([null, 3], [null, 4]).WindowForLevel(6).Should().BeNull();
        UpgradeSchedule.FromArrays(new int?[12], new int?[12]).IsEmpty.Should().BeTrue();
        UpgradeSchedule.FromArrays(null, null).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Empty_HasNoWindowsAndSaysUnset()
    {
        UpgradeSchedule.Empty.IsEmpty.Should().BeTrue();
        UpgradeSchedule.Empty.Describe().Should().Be("未设置");
        UpgradeSchedule.Empty.DescribeLines().Should().BeEmpty();
        UpgradeSchedule.Empty.HasOrderingIssue.Should().BeFalse();
        UpgradeSchedule.Empty.LevelsAt(5).Should().BeEmpty();
    }

    [Fact]
    public void WindowForLevel_IgnoresLevelsOutsideTwoToSix()
    {
        Ordered.WindowForLevel(0).Should().BeNull();
        Ordered.WindowForLevel(1).Should().BeNull();
        Ordered.WindowForLevel(7).Should().BeNull();
    }

    [Fact]
    public void LevelsAt_ReturnsLevelsWhoseWindowContainsTheRound()
    {
        Ordered.LevelsAt(3).Should().Equal(2);
        Ordered.LevelsAt(4).Should().Equal(2);
        Ordered.LevelsAt(5).Should().Equal(3);
        Ordered.LevelsAt(11).Should().BeEmpty();
        Ordered.LevelsAt(16).Should().Equal(6);
        Ordered.LevelsAt(21).Should().BeEmpty();
        Ordered.LevelsAt(0).Should().BeEmpty();
        Ordered.LevelsAt(31).Should().BeEmpty();
    }

    [Fact]
    public void LevelsAt_ReportsEveryLevelWhenWindowsOverlap()
    {
        var schedule = UpgradeSchedule.FromArrays([null, 4, 4], [null, 6, 8]);

        schedule.LevelsAt(5).Should().Equal(2, 3);
    }

    [Fact]
    public void ToArrays_RoundTripBackToWindows()
    {
        var from = Ordered.ToFromArray();
        var to = Ordered.ToToArray();

        from.Should().HaveCount(UpgradeSchedule.LevelCount);
        from[0].Should().BeNull();
        from[1].Should().Be(3);
        to[5].Should().Be(20);

        var again = UpgradeSchedule.FromArrays(from, to);
        again.Describe().Should().Be(Ordered.Describe());
    }

    [Fact]
    public void ToArrays_ReturnsCopies()
    {
        var first = Ordered.ToFromArray();
        first[1] = 99;

        Ordered.WindowForLevel(2)!.Value.From.Should().Be(3);
    }

    [Fact]
    public void Describe_ListsOnlyPlannedLevels()
    {
        var schedule = UpgradeSchedule.FromArrays([null, 3, null, 8], [null, 4, null, 10]);

        schedule.Describe().Should().Be("2本 3–4 · 4本 8–10");
        schedule.DescribeLines().Should().Equal("2 本 · 第 3–4 回合", "4 本 · 第 8–10 回合");
    }

    [Fact]
    public void HasOrderingIssue_FlagsLaterLevelStartingBeforePreviousEnds()
    {
        UpgradeSchedule.FromArrays([null, 8, 4], [null, 10, 6]).HasOrderingIssue.Should().BeTrue();
        Ordered.HasOrderingIssue.Should().BeFalse();
    }

    [Fact]
    public void HasOrderingIssue_AllowsOverlappingWindows()
    {
        UpgradeSchedule.FromArrays([null, 4, 5], [null, 8, 9]).HasOrderingIssue.Should().BeFalse();
    }

    [Fact]
    public void RoundWindow_CountsAndDescribesItself()
    {
        var window = new RoundWindow(5, 7);
        window.Count.Should().Be(3);
        window.Contains(4).Should().BeFalse();
        window.Contains(5).Should().BeTrue();
        window.Contains(7).Should().BeTrue();
        window.Contains(8).Should().BeFalse();
        window.Short.Should().Be("5–7");
        window.Describe().Should().Be("第 5–7 回合");

        new RoundWindow(9, 9).Short.Should().Be("9");
    }

    [Fact]
    public void Lineup_UpgradePlanIsBuiltFromItsArrays()
    {
        var lineup = new Lineup
        {
            UpgradeFrom = [null, 4, null, null, null, null],
            UpgradeTo = [null, 6, null, null, null, null],
        };

        lineup.UpgradePlan.WindowForLevel(2).Should().Be(new RoundWindow(4, 6));
        lineup.UpgradePlanText.Should().Be("2本 4–6");

        new Lineup().UpgradePlanText.Should().BeEmpty();
    }

    [Fact]
    public void Lineup_JsonRoundTripKeepsTheWindows()
    {
        var lineup = new Lineup
        {
            UpgradeFrom = [null, null, 8, null, 12, null],
            UpgradeTo = [null, null, 10, null, 14, null],
        };

        var json = JsonSerializer.Serialize(lineup);
        json.Should().Contain("\"upgradeFrom\"");
        json.Should().Contain("\"upgradeTo\"");

        var copy = JsonSerializer.Deserialize<Lineup>(json)!;
        copy.UpgradePlan.WindowForLevel(3).Should().Be(new RoundWindow(8, 10));
        copy.UpgradePlan.WindowForLevel(5).Should().Be(new RoundWindow(12, 14));
        copy.UpgradePlan.WindowForLevel(2).Should().BeNull();
    }

    [Fact]
    public void Lineup_OldSaveWithoutPlanStaysEmptyButKeepsRemindersOn()
    {
        var lineup = JsonSerializer.Deserialize<Lineup>("{\"name\":\"旧格式\",\"slots\":[]}")!;

        lineup.UpgradeReminderEnabled.Should().BeTrue();
        lineup.UpgradeFrom.Should().HaveCount(UpgradeSchedule.LevelCount);
        lineup.UpgradePlan.IsEmpty.Should().BeTrue();
        lineup.UpgradePlanText.Should().BeEmpty();
    }
}
