using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public sealed class LineupListTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aspa-lineup-list-" + Guid.NewGuid().ToString("N"));
    private readonly LineupStore _store;
    private readonly LineupViewModel _vm;

    public LineupListTests()
    {
        _store = new LineupStore(_directory, new LineupEquipmentService(new Dictionary<string, string>()));
        _vm = new LineupViewModel(_store, new CovenantCalculator([], [], new Dictionary<string, string>()));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void AvailabilityOnlyUsesLateOperatorsAndRefreshesAfterBanChanges()
    {
        var bans = new BanViewModel();
        _vm.SetBanChecker(bans.IsBanned);
        _vm.LoadLineups([
            new() { Id = "a", Slots = [new() { OperatorName = "角峰" }], EarlySlots = [new() { OperatorName = "灰喉" }],
                MidSlots = [new() { OperatorName = "灰喉" }], SupportSlots = [new() { OperatorName = "灰喉" }] },
            new() { Id = "b", Slots = [new() { OperatorName = "灰喉" }] }
        ]);
        bans.SetBans(["灰喉"]);
        _vm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
        _vm.VisibleLineups.Select(l => l.Id).Should().Equal("a");
        _vm.SelectedAvailabilityFilter = AvailabilityFilter.Unavailable;
        _vm.VisibleLineups.Select(l => l.Id).Should().Equal("b");
        bans.ClearBans();
        _vm.RefreshAvailability();
        _vm.VisibleLineups.Should().BeEmpty();
        _vm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
        _vm.VisibleLineups.Should().HaveCount(2);
    }

    [Fact]
    public void CovenantBansUseTheSameAllCovenantsRuleAsOperators()
    {
        var op = new Operator { Name = "霍尔海雅", AdditionalCovenants = ["精准", "奥术"] };
        var bans = new HashSet<string> { "精准" };
        _vm.SetBanChecker(name => name == op.Name && CovenantBanRules.IsExcluded(op, bans));
        _vm.LoadLineups([new() { Slots = [new() { OperatorName = op.Name }] }]);
        _vm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
        _vm.VisibleLineups.Should().ContainSingle();
        bans.Add("奥术");
        _vm.RefreshAvailability();
        _vm.VisibleLineups.Should().BeEmpty();
    }

    [Fact]
    public async Task ManualMovesSurviveReloadWithoutChangingLineupFilesOrEditOrder()
    {
        var lineups = new[] { new Lineup { Id = "a" }, new Lineup { Id = "b" }, new Lineup { Id = "c" } };
        foreach (var lineup in lineups) await _store.SaveAsync(lineup);
        var contents = lineups.ToDictionary(l => l.Id, l => File.ReadAllText(Path.Combine(_directory, l.Id + ".json")));
        _vm.LoadLineups(lineups);
        await _vm.MoveAsync(lineups[2], -1);
        _vm.Lineups.Select(l => l.Id).Should().Equal("a", "c", "b");
        await _vm.MoveAsync(lineups[0], 1);
        _vm.Lineups.Select(l => l.Id).Should().Equal("c", "a", "b");
        await _vm.MoveAsync(lineups[2], 0);
        (await _store.LoadAllAsync()).Select(l => l.Id).Should().Equal("c", "a", "b");
        foreach (var lineup in lineups)
            File.ReadAllText(Path.Combine(_directory, lineup.Id + ".json")).Should().Be(contents[lineup.Id]);
        await _vm.AddOrUpdateAsync(new Lineup { Id = "b", Name = "edited" });
        (await _store.LoadAllAsync()).Select(l => l.Id).Should().Equal("c", "a", "b");
        File.ReadAllText(Path.Combine(_directory, "order", "lineups.json.bak")).Should().Contain("a");
    }

    [Fact]
    public async Task FilteredMovesSwapVisibleNeighborsAndTopUsesTheFullList()
    {
        var first = new Lineup { Id = "a", Slots = [new() { OperatorName = "可用" }] };
        var hidden = new Lineup { Id = "hidden", Slots = [new() { OperatorName = "禁用" }] };
        var last = new Lineup { Id = "b", Slots = [new() { OperatorName = "可用" }] };
        _vm.SetBanChecker(name => name == "禁用");
        _vm.LoadLineups([first, hidden, last]);
        _vm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
        await _vm.MoveAsync(last, -1);
        _vm.Lineups.Select(l => l.Id).Should().Equal("b", "hidden", "a");
        _vm.VisibleLineups.Select(l => l.Id).Should().Equal("b", "a");
        await _vm.MoveAsync(first, 0);
        _vm.Lineups.Select(l => l.Id).Should().Equal("a", "b", "hidden");
        _vm.SelectedAvailabilityFilter = AvailabilityFilter.All;
        _vm.VisibleLineups.Select(l => l.Id).Should().Equal("a", "b", "hidden");
    }

    [Fact]
    public async Task NewAndDeletedLineupsKeepTheSavedCustomOrder()
    {
        var first = new Lineup { Id = "a" };
        var second = new Lineup { Id = "b" };
        await _vm.AddOrUpdateAsync(first);
        await _vm.AddOrUpdateAsync(second);
        await _vm.MoveAsync(first, 0);
        await _vm.AddOrUpdateAsync(new Lineup { Id = "c" });
        (await _store.LoadAllAsync()).Select(l => l.Id).Should().Equal("c", "a", "b");
        await _vm.DeleteAsync(first);
        (await _store.LoadAllAsync()).Select(l => l.Id).Should().Equal("c", "b");
    }

    [Fact]
    public async Task BoundaryMovesAreIgnoredAndSaveFailuresRestoreTheOrder()
    {
        var first = new Lineup { Id = "a" };
        var second = new Lineup { Id = "b" };
        _vm.LoadLineups([first, second]);
        _vm.CanMove(first, -1).Should().BeFalse();
        _vm.CanMove(first, 0).Should().BeFalse();
        _vm.CanMove(second, 1).Should().BeFalse();
        await _vm.MoveAsync(first, -1);
        await _vm.MoveAsync(second, 1);
        await File.WriteAllTextAsync(Path.Combine(_directory, "order"), "blocks the order directory");
        await Assert.ThrowsAsync<IOException>(() => _vm.MoveAsync(second, -1));
        _vm.Lineups.Select(l => l.Id).Should().Equal("a", "b");
    }

    [Fact]
    public async Task ProfilesWithoutSavedOrderStillLoadByUpdateTime()
    {
        var older = new Lineup { Id = "older", UpdatedAt = new DateTime(2020, 1, 1) };
        var newer = new Lineup { Id = "newer", UpdatedAt = new DateTime(2021, 1, 1) };
        foreach (var lineup in new[] { older, newer })
            await File.WriteAllTextAsync(Path.Combine(_directory, lineup.Id + ".json"), JsonSerializer.Serialize(lineup));
        (await _store.LoadAllAsync()).Select(l => l.Id).Should().Equal("newer", "older");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
