// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class TrackingSourcesTests
{
    [Fact]
    public void ClearingHighlightsKeepsTrackingAndSourceLinksAndRetrackingLightsUpAgain()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        var lineup = new Lineup { Id = "a", Slots = [new() { OperatorName = "甲" }] };
        vm.TrackLineup(lineup);
        vm.ClearLineupHighlights();
        vm.TrackedLineupIds.Should().BeEmpty();
        vm.IsLineupTracked("a").Should().BeTrue();
        vm.TrackedOperators.Should().ContainSingle().Which.Name.Should().Be("甲");
        var restored = new TrackingViewModel { TrackMultipleLineups = true };
        restored.LoadState(vm.CaptureState());
        restored.TrackedLineupIds.Should().BeEmpty();
        restored.ApplyTrackedLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "乙" }] });
        restored.TrackedLineupIds.Should().BeEmpty();
        restored.TrackedOperators.Should().ContainSingle().Which.Name.Should().Be("乙");
        restored.TrackLineup(lineup);
        restored.TrackedLineupIds.Should().BeEquivalentTo("a");
    }

    [Fact]
    public void ApplyingOneLineupPreservesSharedTargetsManualTargetsAndOtherLineups()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.LoadEntries([new() { Name = "共用", Equipments = ["手动需求"] }]);
        vm.AddTracking("手动装备", TrackingType.Equipment);
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "共用", Equipments = ["A装备"] }, new() { OperatorName = "仅A" }] });
        vm.TrackLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "共用", IsElite = true, Equipments = ["B装备"] }, new() { OperatorName = "仅B" }] });
        vm.TrackedLineupIds.Should().BeEquivalentTo("a", "b");
        vm.TrackMultipleLineups = false; // Editing applies to this source even if the setting changes afterwards.
        vm.ApplyTrackedLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "新A" }] });
        vm.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo("共用", "仅B", "新A");
        vm.TrackedOperators.Single(e => e.Name == "共用").IsElite.Should().BeTrue();
        vm.TrackedEquipment.Select(e => e.Name).Should().BeEquivalentTo("手动需求", "B装备", "手动装备");
        vm.TrackedLineupIds.Should().BeEquivalentTo("a", "b");
        vm.ApplyTrackedLineup(new Lineup { Id = "b" });
        vm.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo("共用", "新A");
        vm.TrackedOperators.Single(e => e.Name == "共用").Equipments.Should().Equal("手动需求");
        vm.TrackedEquipment.Select(e => e.Name).Should().BeEquivalentTo("手动需求", "手动装备");
        vm.TrackedLineupIds.Should().BeEquivalentTo("a");
    }

    [Fact]
    public void ManualAdditionAndRouteRequirementsSurviveLineupEdits()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "手动保留" }, new() { OperatorName = "路线保留" }] });
        vm.AddTracking("手动保留", TrackingType.Operator);
        vm.TrackRoute(new PlannedRoute { Stages = [new RouteStage(new("大成", 6, 6, 0, 1),
            [new RouteOperator("路线保留", 1, "", RouteOperatorRole.Final)], []) { IsFinal = true }] });
        vm.ApplyTrackedLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "新目标" }] });
        vm.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo("手动保留", "路线保留", "新目标");
    }

    [Fact]
    public void StateChangesDoNotCreateManualOriginsAndRemovedTargetsDoNotReturn()
    {
        var state = new DeployPoolState();
        var vm = new TrackingViewModel();
        vm.SetDeployPool(state);
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "旧目标", IsElite = true }] });
        state.SetOwned("旧目标", true);
        state.SetElite("旧目标", true);
        vm.SyncDeployPool();
        state.SetElite("旧目标", false);
        vm.SyncDeployPool();
        vm.TrackedOperators.Single().Origins.Should().OnlyContain(o => o.LineupId == "a");
        vm.ApplyTrackedLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "新目标" }] });
        vm.TrackedOperators.Should().ContainSingle().Which.Name.Should().Be("新目标");
        state.IsOwned("旧目标").Should().BeTrue();
    }

    [Fact]
    public void ReplacementClearingAndRemovingLastTargetResetHighlights()
    {
        var vm = new TrackingViewModel();
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "甲" }] });
        vm.TrackLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "乙" }] });
        vm.TrackedLineupIds.Should().BeEquivalentTo("b");
        vm.RemoveTracking("乙");
        vm.TrackedLineupIds.Should().BeEmpty();
        vm.TrackLineup(new Lineup { Id = "c", Slots = [new() { OperatorName = "丙" }] });
        vm.ClearAllTracking();
        vm.TrackedLineupIds.Should().BeEmpty();
        Action apply = () => vm.ApplyTrackedLineup(new Lineup { Id = "missing" });
        apply.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task AtomicStateRoundTripKeepsAssociationsAndMakesLegacyBackup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tracking-origins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var settings = new SettingsManager(folder);
        const string legacy = "[{\"name\":\"旧记录\",\"type\":0}]";
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "tracking.json"), legacy);
            var vm = new TrackingViewModel { TrackMultipleLineups = true };
            vm.LoadState(await settings.LoadTrackingStateAsync(EquipmentTestData.Rules));
            vm.TrackedLineupIds.Should().BeEmpty("legacy operator overlap must not guess the source lineup");
            vm.TrackLineup(new Lineup { Id = "a", Name = "阵容A", Slots = [new() { OperatorName = "角峰", IsElite = true }] });
            await settings.SaveTrackingStateAsync(vm.CaptureState());
            var backup = Directory.GetFiles(folder, "*.pre-sources-*.bak").Should().ContainSingle().Which;
            (await File.ReadAllTextAsync(backup)).Should().Be(legacy);
            var restored = new TrackingViewModel();
            restored.LoadState(await settings.LoadTrackingStateAsync(EquipmentTestData.Rules));
            restored.TrackedLineupIds.Should().BeEquivalentTo("a");
            restored.TrackedOperators.Single(e => e.Name == "角峰").Origins.Should().ContainSingle().Which.LineupId.Should().Be("a");
            restored.ApplyTrackedLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "流明" }] });
            restored.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo("旧记录", "流明");
            restored.ClearLineupHighlights();
            await settings.SaveTrackingStateAsync(restored.CaptureState());
            var hidden = new TrackingViewModel();
            hidden.LoadState(await settings.LoadTrackingStateAsync(EquipmentTestData.Rules));
            hidden.TrackedLineupIds.Should().BeEmpty();
            hidden.IsLineupTracked("a").Should().BeTrue();
            Directory.GetFiles(folder, "*.pre-sources-*.bak").Should().ContainSingle();
            Directory.GetFiles(folder, "*.tmp").Should().BeEmpty();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ApplyingNewTargetsPreservesOwnedEliteAndDuplicateCounts()
    {
        var state = new DeployPoolState { Owned = ["角峰"], Elite = ["角峰"], Counts = { ["角峰"] = 2 } };
        var vm = new TrackingViewModel();
        vm.SetDeployPool(state);
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "角峰" }] });
        vm.ApplyTrackedLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "角峰", IsElite = true }] });
        vm.IsTracked("角峰").Should().BeFalse();
        state.CountOf("角峰").Should().Be(2);
        state.IsElite("角峰").Should().BeTrue();
    }
}
