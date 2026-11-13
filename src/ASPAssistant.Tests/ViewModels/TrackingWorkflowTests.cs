// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class TrackingWorkflowTests
{
    [Fact]
    public void DirectLineupTracksFinalAndTransitionWithPhaseNamesButNoGuessedExpiry()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 6 };
        vm.TrackLineup(new Lineup
        {
            Slots = [new() { OperatorName = "大成", IsElite = true }],
            MidSlots = [new() { OperatorName = "中前期", Equipments = ["中期装备"] }],
            EarlySlots = [new() { OperatorName = "大成" }, new() { OperatorName = "中前期", Equipments = ["前期装备"] }],
            SupportSlots = [new() { OperatorName = "挂件" }]
        });
        vm.TrackedOperators.Should().HaveCount(3);
        vm.TrackedOperators.Single(e => e.Name == "大成").Should().Match<TrackingEntry>(e => e.Role == TrackingRole.Final && e.IsElite);
        var transition = vm.TrackedOperators.Single(e => e.Name == "中前期");
        transition.Role.Should().Be(TrackingRole.Transition);
        transition.Stages.Should().Be("前期 · 中期");
        transition.Equipments.Should().Equal("中期装备");
        vm.TrackedOperators.Single(e => e.Name == "挂件").Stages.Should().Be("挂件");
        vm.TrackedOperators.Should().OnlyContain(e => !e.IsExpired && e.ExpiresAt == 0);
    }

    [Fact]
    public void MergedTeamsPromoteTransitionAndKeepExistingFinalRequirement()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup { EarlySlots = [new() { OperatorName = "角峰" }] });
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰", IsElite = true, Equipments = ["盾牌"] }] });
        vm.TrackLineup(new Lineup { EarlySlots = [new() { OperatorName = "角峰", Equipments = ["过渡装备"] }] });
        var entry = vm.TrackedOperators.Should().ContainSingle().Which;
        entry.Role.Should().Be(TrackingRole.Final);
        entry.IsElite.Should().BeTrue();
        entry.Equipments.Should().BeEquivalentTo("盾牌", "过渡装备");
        vm.TrackedEquipment.Select(e => e.Name).Should().BeEquivalentTo("盾牌", "过渡装备");
    }

    [Theory]
    [InlineData(false, false, false, "待获取")]
    [InlineData(false, true, false, "已完成")]
    [InlineData(true, false, false, "待获取")]
    [InlineData(true, true, false, "待精锐")]
    [InlineData(true, true, true, "已完成")]
    public void ProgressDescribesTargetCompletion(bool targetElite, bool owned, bool elite, string expected)
    {
        var state = new DeployPoolState();
        state.SetOwned("角峰", owned);
        state.SetElite("角峰", elite);
        var vm = new TrackingViewModel();
        vm.SetDeployPool(state);
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰", IsElite = targetElite }] });
        vm.OperatorProgress(vm.TrackedOperators.Single()).Should().Be(expected);
    }

    [Fact]
    public void ManualTrackingRespectsOwnedStateAndClearingDoesNotChangeThePool()
    {
        var state = new DeployPoolState { Owned = ["角峰"], Elite = ["角峰"], Counts = { ["角峰"] = 2 } };
        var vm = new TrackingViewModel();
        vm.SetDeployPool(state);
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.IsTracked("角峰").Should().BeFalse();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.IsTracked("角峰").Should().BeFalse();
        vm.ClearAllTracking();
        state.CountOf("角峰").Should().Be(2);
        state.IsElite("角峰").Should().BeTrue();
    }

    [Fact]
    public void LoadingLegacyEntriesPreservesTheirGroupingAndDoesNotInventPhases()
    {
        var vm = new TrackingViewModel();
        vm.LoadEntries([new() { Name = "旧记录", Role = TrackingRole.Final }]);
        vm.TrackedOperators.Should().ContainSingle().Which.Role.Should().Be(TrackingRole.Final);
        vm.TrackedOperators.Single().Stages.Should().BeEmpty();
    }
}
