// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Models;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace ASPAssistant.Tests.Models;

public class UpgradeScheduleTests
{
    // 与 LineupStore 落盘时一致（缩进、大小写不敏感），用来验证存档格式。
    private static readonly JsonSerializerOptions StoreOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void FromArray_KeepsAllSixLevels()
    {
        var schedule = UpgradeSchedule.FromArray([3, null, 7, null, null, 12]);

        schedule.RoundForLevel(1).Should().Be(3);
        schedule.RoundForLevel(2).Should().BeNull();
        schedule.RoundForLevel(3).Should().Be(7);
        schedule.RoundForLevel(6).Should().Be(12);
        schedule.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void FromArray_PadsShortAndTruncatesLongInput()
    {
        UpgradeSchedule.FromArray([4]).RoundForLevel(6).Should().BeNull();
        UpgradeSchedule.FromArray([1, 2, 3, 4, 5, 6, 7]).ToArray().Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(31)]
    [InlineData(99)]
    public void FromArray_DropsOutOfRangeRounds(int bad)
    {
        var schedule = UpgradeSchedule.FromArray([bad]);
        schedule.RoundForLevel(1).Should().BeNull();
        schedule.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void FromArray_NullOrEmptyIsEmpty()
    {
        UpgradeSchedule.FromArray(null).IsEmpty.Should().BeTrue();
        UpgradeSchedule.FromArray([]).IsEmpty.Should().BeTrue();
        UpgradeSchedule.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void LevelsAt_ReturnsEveryLevelPlannedForThatRound()
    {
        var schedule = UpgradeSchedule.FromArray([3, 8, 8, null, 15, 15]);

        schedule.LevelsAt(8).Should().Equal(2, 3);
        schedule.LevelsAt(15).Should().Equal(5, 6);
        schedule.LevelsAt(4).Should().BeEmpty();
        schedule.LevelsAt(0).Should().BeEmpty();
        schedule.LevelsAt(31).Should().BeEmpty();
    }

    [Fact]
    public void RoundForLevel_IgnoresLevelsOutsideOneToSix()
    {
        var schedule = UpgradeSchedule.FromArray([3, 4, 5, 6, 7, 8]);

        schedule.RoundForLevel(0).Should().BeNull();
        schedule.RoundForLevel(7).Should().BeNull();
    }

    [Fact]
    public void Describe_ListsOnlyConfiguredLevels()
    {
        UpgradeSchedule.Empty.Describe().Should().Be("未设置");
        UpgradeSchedule.FromArray([null, 4, null, 8, null, null])
            .Describe().Should().Be("2本 第4 · 4本 第8");
    }

    [Fact]
    public void ToArray_ReturnsIndependentCopy()
    {
        var schedule = UpgradeSchedule.FromArray([5]);
        var copy = schedule.ToArray();
        copy[0] = 9;

        schedule.RoundForLevel(1).Should().Be(5);
    }

    [Fact]
    public void Lineup_UpgradePlanIsBuiltFromUpgradeRounds()
    {
        var lineup = new Lineup { UpgradeRounds = [null, 6, null, null, null, null] };

        lineup.UpgradePlan.RoundForLevel(2).Should().Be(6);
        lineup.UpgradeReminderEnabled.Should().BeTrue();
    }

    [Fact]
    public void Lineup_SurvivesJsonRoundTripWithSchedule()
    {
        var lineup = new Lineup
        {
            Name = "八回合三本",
            UpgradeRounds = [null, null, 8, null, 12, null],
            UpgradeReminderEnabled = true,
        };

        var json = JsonSerializer.Serialize(lineup, StoreOptions);
        json.Should().Contain("\"upgradeRounds\"");

        var copy = JsonSerializer.Deserialize<Lineup>(json, StoreOptions)!;
        copy.UpgradePlan.RoundForLevel(3).Should().Be(8);
        copy.UpgradePlan.RoundForLevel(5).Should().Be(12);
        copy.UpgradePlan.RoundForLevel(1).Should().BeNull();
    }

    [Fact]
    public void Lineup_MissingFieldsFallBackToDefaults()
    {
        var lineup = JsonSerializer.Deserialize<Lineup>("{\"name\":\"旧存档\"}", StoreOptions)!;

        lineup.UpgradePlan.IsEmpty.Should().BeTrue();
        lineup.UpgradeReminderEnabled.Should().BeTrue();
        lineup.UpgradeRounds.Should().HaveCount(UpgradeSchedule.LevelCount);
    }

    [Fact]
    public void Lineup_UsesAGuessableJsonFieldName()
    {
        var json = JsonSerializer.Serialize(new Lineup(), StoreOptions);

        json.Should().Contain("\"upgradeRounds\"");
        json.Should().Contain("\"upgradeReminderEnabled\"");
        json.Should().NotContain("upgradeRoundFrom");
    }
}
