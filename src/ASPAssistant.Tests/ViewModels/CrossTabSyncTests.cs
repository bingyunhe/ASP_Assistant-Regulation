using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameState;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class CrossTabSyncTests
{
    [Fact]
    public void NewRunResetsProgressAndShopHighlightsWithoutLosingGoalsOrSources()
    {
        var pool = new DeployPoolState { Owned = ["角峰"], Elite = ["角峰"], Counts = { ["角峰"] = 3 }, Extra = ["隐现"] };
        var vm = new TrackingViewModel { TrackMultipleLineups = true, DetectedTerminalLevel = 6, IsTerminalLevelRestored = true };
        vm.SetDeployPool(pool);
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "角峰", IsElite = true, Equipments = ["盾牌"] }] });
        vm.AddTracking("手动装备", TrackingType.Equipment);
        vm.ToggleAcquired("手动装备");
        vm.ManualTerminalLevel = 5;
        vm.StartNewRun();

        pool.Owned.Should().BeEmpty();
        pool.Elite.Should().BeEmpty();
        pool.Counts.Should().BeEmpty();
        pool.Extra.Should().Equal("隐现");
        vm.TerminalLevel.Should().Be(0);
        vm.IsTerminalLevelRestored.Should().BeFalse();
        vm.TrackedLineupIds.Should().BeEquivalentTo("a");
        vm.TrackedOperators.Should().ContainSingle().Which.IsElite.Should().BeTrue();
        vm.AllEntries.Should().OnlyContain(e => !e.IsAcquired);
        vm.TrackedEquipment.Select(e => e.Name).Should().BeEquivalentTo("盾牌", "手动装备");
        var shop = new List<ShopItem> { new() { Name = "角峰" }, new() { Name = "盾牌" }, new() { Name = "手动装备" } };
        GameStateUpdater.UpdateShopTracking(shop, vm.ActiveEntries.ToList());
        shop.Should().OnlyContain(e => e.IsTracked);
    }

    [Fact]
    public void AcquiredTransformationSurvivesResumeSourceEditAndPersistence()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        var a = new Lineup { Id = "a", Slots = [new() { OperatorName = "角峰", Equipments = ["变形同构体", "炎国短刀"] }] };
        vm.TrackLineup(a);
        vm.TrackLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "隐现", Equipments = ["炎国短刀"] }] });
        vm.ToggleAcquired("炎国短刀");
        vm.TrackedOperators.Single(e => e.Name == "角峰").Equipments.Should().Equal("变形同构体", "炎国短刀");
        var restored = new TrackingViewModel { TrackMultipleLineups = true };
        restored.LoadState(JsonSerializer.Deserialize<TrackingState>(JsonSerializer.Serialize(vm.CaptureState()))!);
        restored.ApplyTrackedLineup(new Lineup { Id = "b" });
        restored.IsTracked("炎国短刀").Should().BeFalse();
        restored.ToggleAcquired("炎国短刀");
        restored.IsTracked("炎国短刀").Should().BeTrue();
        restored.TrackedOperators.Single().Origins.Single().Equipments.Should().Equal("变形同构体", "炎国短刀");
        restored.StartNewRun();
        restored.IsTracked("炎国短刀").Should().BeTrue();
    }

    [Fact]
    public void RemovingFinalSourceImmediatelyExpiresTheRemainingRouteTarget()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true, DetectedTerminalLevel = 2 };
        vm.TrackRoute(new PlannedRoute { Stages = [Stage("开局", 2, false, "角峰"), Stage("大成", 4, true, "隐现")] });
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "角峰" }] });
        vm.DetectedTerminalLevel = 6;
        vm.ApplyTrackedLineup(new Lineup { Id = "a" });
        vm.TrackedOperators.Single(e => e.Name == "角峰").IsExpired.Should().BeTrue();
        vm.ActiveEntries.Should().NotContain(e => e.Name == "角峰");
        var shop = new List<ShopItem> { new() { Name = "角峰" } };
        GameStateUpdater.UpdateShopTracking(shop, vm.ActiveEntries.ToList());
        shop.Single().IsTracked.Should().BeFalse();
    }

    [Fact]
    public void RouteRefreshPreservesManualAndLineupRequirementsAcrossReload()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.LoadEntries([new() { Name = "角峰", Equipments = ["手动装备"] }]);
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "角峰", Equipments = ["阵容装备"] }] });
        var route = new PlannedRoute { Stages = [Stage("大成", 6, true, "角峰")] };
        vm.TrackRoute(route);
        vm.TrackRoute(route);
        var restored = new TrackingViewModel { TrackMultipleLineups = true };
        restored.LoadState(JsonSerializer.Deserialize<TrackingState>(JsonSerializer.Serialize(vm.CaptureState()))!);
        restored.TrackRoute(route);
        restored.ApplyTrackedLineup(new Lineup { Id = "a" });
        restored.TrackedOperators.Single().Origins.Should().HaveCount(2);
        restored.TrackedOperators.Single().Equipments.Should().Equal("手动装备");
        restored.IsTracked("手动装备").Should().BeTrue();
        restored.AddTracking("角峰", TrackingType.Operator);
        restored.TrackedOperators.Single().Origins.Should().HaveCount(2);
    }

    [Fact]
    public void ManualAdditionToRouteRemainsAfterRouteRequirementsChange()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackRoute(new PlannedRoute { Stages = [Stage("大成", 6, true, "角峰")] });
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.TrackRoute(new PlannedRoute { Stages = [Stage("开局", 2, false, "角峰"), Stage("大成", 4, true, "隐现")] });
        vm.DetectedTerminalLevel = 6;
        vm.TrackedOperators.Single(e => e.Name == "角峰").Role.Should().Be(TrackingRole.Final);
        vm.ActiveEntries.Should().Contain(e => e.Name == "角峰");
    }

    [Fact]
    public void AddingAnotherRouteDoesNotDowngradeAnExistingFinalTarget()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackRoute(new PlannedRoute { Stages = [Stage("大成", 6, true, "角峰")] });
        var transition = new PlannedRoute { Stages = [Stage("开局", 2, false, "角峰"), Stage("大成", 4, true, "隐现")] };
        vm.TrackRoute(transition);
        vm.TrackRoute(transition);
        vm.DetectedTerminalLevel = 6;
        vm.TrackedOperators.Single(e => e.Name == "角峰").Role.Should().Be(TrackingRole.Final);
        vm.TrackedOperators.Single(e => e.Name == "角峰").Origins.Should().HaveCount(2);
    }

    [Fact]
    public void ClearingRunBansRemovesAutomaticRestrictionsAndKeepsManualChoices()
    {
        var bans = new BanViewModel();
        bans.SetBans(["自动禁用", "手动重选"]);
        bans.SetCovenantBans(["炎"]);
        bans.ToggleBan("手动禁用");
        bans.ToggleBan("手动重选");
        bans.ToggleBan("手动重选");
        var changes = 0;
        bans.BansChanged += () => changes++;
        bans.ClearRunBans();
        bans.BannedNames.Should().BeEquivalentTo("手动禁用", "手动重选");
        bans.BannedCovenants.Should().BeEmpty();
        changes.Should().Be(1);
        bans.SetCovenantBans(["炎"]);
        bans.ClearBans();
        bans.BannedNames.Should().BeEmpty();
        bans.BannedCovenants.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentPoolSavesCannotOverwriteTheNewRunWithOldProgress()
    {
        var folder = Path.Combine(Path.GetTempPath(), "run-sync-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsManager(folder);
        var pool = new DeployPoolState { Owned = ["角峰"], Elite = ["角峰"], Counts = { ["角峰"] = 2 } };
        try
        {
            var saves = new List<Task>();
            for (var i = 0; i < 10; i++) saves.Add(settings.SaveDeployPoolAsync(pool));
            var vm = new TrackingViewModel();
            vm.SetDeployPool(pool);
            vm.StartNewRun();
            saves.Add(settings.SaveDeployPoolAsync(pool));
            await Task.WhenAll(saves);
            var restored = await settings.LoadDeployPoolAsync();
            restored.Owned.Should().BeEmpty();
            restored.Elite.Should().BeEmpty();
            restored.Counts.Should().BeEmpty();
            JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "deploy-pool.json.bak"))).Dispose();
            Directory.GetFiles(folder, "*.tmp").Should().BeEmpty();
        }
        finally { Directory.Delete(folder, true); }
    }

    private static RouteStage Stage(string name, int level, bool final, string op) =>
        new(new(name, level, level, 0, 1), [new(op, 1, "", RouteOperatorRole.Final)], []) { IsFinal = final };
}
