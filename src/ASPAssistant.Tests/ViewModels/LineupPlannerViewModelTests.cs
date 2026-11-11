using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class LineupPlannerViewModelTests
{
    [Fact]
    public async Task LimitedSearchDoesNotClaimUnsolvedVariantsAreImpossible()
    {
        var operators = Enumerable.Range(0, 49).Select(i => new Operator
        {
            Name = $"干员{i}", CoreCovenants = ["炎"], AdditionalCovenants = ["独行"]
        }).ToList();
        CovenantInfo[] covenants =
        [
            new() { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new() { Name = "独行", BondType = "REGULAR", ActivateCount = 1 }
        ];
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, new BanViewModel(), EquipmentTestData.Rules)
        { DebounceMilliseconds = 0 };
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        Item(vm, "独行").ToggleLevel1Command.Execute(null);
        vm.IsActive = true;
        await vm.PendingSearch!;

        vm.TotalResults.Should().Be(0);
        vm.VariantCells.Should().Contain(c => c.IsLimited);
        vm.StatusDetail.Should().Contain("搜索上限");
        vm.ResultHint.Should().Contain("搜索受限").And.NotContain("无解");
        vm.VariantCells.Should().OnlyContain(c => !c.ToolTip.Contains("无解"));
    }

    private static Operator Op(string name, params string[] core) => new() { Name = name, CoreCovenants = [.. core], IconPath = $"icons/operators/{name}.png" };

    private static (LineupPlannerViewModel Vm, BanViewModel Bans) Create()
    {
        var operators = new List<Operator>();
        for (var i = 1; i <= 6; i++) operators.Add(Op($"炎{i}", "炎"));
        for (var i = 1; i <= 6; i++) operators.Add(Op($"维{i}", "维多利亚"));
        operators.Add(new Operator { Name = "炎突", CoreCovenants = ["炎"], AdditionalCovenants = ["突袭"] });
        operators.Add(new Operator { Name = "维突", CoreCovenants = ["维多利亚"], AdditionalCovenants = ["突袭"] });
        var covenants = new[]
        {
            new CovenantInfo { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new CovenantInfo { Name = "维多利亚", BondType = "SEASON", ActivateCount = 3 },
            new CovenantInfo { Name = "独行", BondType = "REGULAR", ActivateCount = 1 },
            new CovenantInfo { Name = "突袭", BondType = "REGULAR", ActivateCount = 2 },
        };
        var bans = new BanViewModel();
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, bans, EquipmentTestData.Rules) { DebounceMilliseconds = 0 };
        return (vm, bans);
    }

    private static PlannerCovenantItem Item(LineupPlannerViewModel vm, string name) => vm.AllCovenants.Single(c => c.Name == name);

    [Fact]
    public async Task ToggleLevel_SearchesAndSelectsFirstVariantWithResults()
    {
        var (vm, _) = Create();
        vm.IsActive = true;

        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        vm.SelectedGoalCount.Should().Be(1);
        vm.TotalResults.Should().BeGreaterThan(0);
        vm.SelectedVariant!.HasResults.Should().BeTrue();
        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups[0].Operators.Where(o => !o.IsFree).Should().OnlyContain(o => o.IconPath.Length > 0);
    }

    [Fact]
    public void ClickingActiveLevelAgainClearsIt()
    {
        var (vm, _) = Create();
        var fire = Item(vm, "炎");

        fire.ToggleLevel2Command.Execute(null);
        fire.Level.Should().Be(2);
        fire.ToggleLevel2Command.Execute(null);
        fire.Level.Should().Be(0);
    }

    [Fact]
    public void CovenantWithoutSecondLevelIgnoresLevelTwo()
    {
        var (vm, _) = Create();
        var solo = Item(vm, "独行");

        solo.HasSecond.Should().BeFalse();
        solo.ToggleLevel2Command.Execute(null);
        solo.Level.Should().Be(0);
    }

    [Fact]
    public void ChangesWhileHiddenAreDeferredUntilActive()
    {
        var (vm, _) = Create();

        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        vm.PendingSearch.Should().BeNull();

        vm.IsActive = true;
        vm.PendingSearch.Should().NotBeNull();
    }

    [Fact]
    public async Task BanningASelectedGoalDropsIt()
    {
        var (vm, bans) = Create();
        vm.IsActive = true;
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        bans.SetCovenantBans(["炎"]);
        await vm.PendingSearch!;

        Item(vm, "炎").IsBanned.Should().BeTrue();
        Item(vm, "炎").Level.Should().Be(0);
        vm.SelectedGoalCount.Should().Be(0);
        vm.TotalResults.Should().Be(0);
    }

    [Fact]
    public async Task NewerSearchReplacesOlderOne()
    {
        var (vm, _) = Create();
        vm.DebounceMilliseconds = 50;
        vm.IsActive = true;

        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        var first = vm.PendingSearch!;
        Item(vm, "维多利亚").ToggleLevel1Command.Execute(null);
        await first;
        await vm.PendingSearch!;

        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups.Should().OnlyContain(l => l.Source.Goals.Count == 2);
    }

    [Fact]
    public async Task LockedOperatorsAreAddedFromSearchAndSaved()
    {
        var (vm, _) = Create();
        vm.IsActive = true;
        Item(vm, "炎").ToggleLevel1Command.Execute(null);

        vm.LockSearchText = "维";
        vm.LockSuggestions.Should().NotBeEmpty();
        vm.AddLocked("维3").Should().BeTrue();
        await vm.PendingSearch!;

        Lineup? saved = null;
        vm.SaveRequested += l => saved = l;
        vm.SaveLineupCommand.Execute(vm.VisibleLineups[0]);

        saved.Should().NotBeNull();
        saved!.Slots.Select(s => s.OperatorName).Should().Contain("维3");
        vm.VisibleLineups[0].Operators[0].IsLocked.Should().BeTrue();
    }

    [Fact]
    public void OnlyCurrentRunHidesUnavailableCovenants()
    {
        var (vm, _) = Create();
        vm.SetAvailableCovenants(["炎"]);

        vm.OnlyCurrentRun = true;

        Item(vm, "炎").IsVisible.Should().BeTrue();
        Item(vm, "维多利亚").IsVisible.Should().BeFalse();
        vm.HasRunInfo.Should().BeTrue();
    }

    [Fact]
    public async Task BannedAdditionalCovenantStaysSelectable()
    {
        var (vm, bans) = Create();
        vm.IsActive = true;
        bans.SetCovenantBans(["突袭"]);
        vm.SetAvailableCovenants(["炎", "维多利亚"]);
        vm.OnlyCurrentRun = true;

        var raid = Item(vm, "突袭");
        raid.IsBanned.Should().BeTrue();
        raid.IsBlocked.Should().BeFalse();
        raid.IsVisible.Should().BeTrue();

        raid.ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        raid.Level.Should().Be(1);
        vm.TotalResults.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CovenantDetailsListMembersAndMarkUnavailableOnes()
    {
        var (vm, bans) = Create();
        var fire = Item(vm, "炎");
        fire.Members.Select(m => m.Name).Should().Contain(["炎1", "炎突"]);
        fire.Members.Should().OnlyContain(m => !m.IsUnavailable);

        bans.SetBans(["炎1"]);

        Item(vm, "炎").Members.Single(m => m.Name == "炎1").IsUnavailable.Should().BeTrue();
        Item(vm, "炎").MemberSummary.Should().Contain("本局可用");
        Item(vm, "炎").FirstRequirement.Should().Be("3 人");
        Item(vm, "独行").SecondRequirement.Should().Be("没有 Ⅱ 级");
    }

    [Fact]
    public async Task ResultOperatorsCarryDetailsAndPlannerNotes()
    {
        var (vm, _) = Create();
        vm.IsActive = true;
        vm.AddLocked("维3");
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        var ops = vm.VisibleLineups[0].Operators;
        ops.Where(o => !o.IsFree).Should().OnlyContain(o => o.HasDetails && o.Operator!.Name == o.Name);
        ops.Where(o => o.IsFree).Should().OnlyContain(o => !o.HasDetails);
        ops.Single(o => o.Name == "维3").ExtraInfo.Should().Contain("必带");
        vm.LockedOperators.Single().Operator.Name.Should().Be("维3");
    }

    [Fact]
    public void SavedLineupsOnlyMatchWhenEveryActivatedCovenantSurvivesTheBans()
    {
        var (vm, bans, lineups) = CreateSaved();
        lineups.LoadLineups([
            Saved("双盟约队", "炎1", "炎2", "炎3", "独1", "独2"),
            Saved("纯炎队", "炎1", "炎2", "炎3"),
        ]);

        vm.HasRunInfo.Should().BeFalse();
        vm.MatchingLineups.Should().BeEmpty("the board waits for the run's ban information");

        vm.SetAvailableCovenants(["炎", "维多利亚", "突袭"]);
        vm.MatchingLineups.Select(l => l.Name).Should().BeEquivalentTo("双盟约队", "纯炎队");
        vm.MatchingLineupSummary.Should().Be("符合 2 / 2");

        var both = vm.MatchingLineups.Single(l => l.Name == "双盟约队");
        both.Operators.Should().HaveCount(5);
        both.Covenants.Select(c => c.Text).Should().BeEquivalentTo("炎 3/3", "突袭 2/2");
        both.Summary.Should().Contain("5 人").And.Contain("2 个盟约");
        both.Source.Name.Should().Be("双盟约队");

        // 维多利亚 is present in the team but not activated by the late-phase team, so it does not exclude it.
        bans.SetCovenantBans(["维多利亚"]);
        vm.MatchingLineups.Select(l => l.Name).Should().BeEquivalentTo("双盟约队", "纯炎队");

        // 突袭 is activated by the first team only.
        bans.SetCovenantBans(["突袭"]);
        vm.MatchingLineups.Select(l => l.Name).Should().Equal("纯炎队");
        vm.MatchingLineupSummary.Should().Be("符合 1 / 2");

        bans.SetCovenantBans(["炎"]);
        vm.MatchingLineups.Should().BeEmpty();
        vm.HasMatchingLineups.Should().BeFalse();
        vm.MatchingLineupHint.Should().Contain("没有完全符合");
    }

    [Fact]
    public void SavedLineupBoardShowsTransformationsFromEquipment()
    {
        var (vm, _, lineups) = CreateSaved();
        var lineup = Saved("转职队", "炎1", "炎2", "炎3");
        lineup.Slots[0].Equipments = ["变形同构体", "炎国短刀"];
        lineups.LoadLineups([lineup]);
        vm.SetAvailableCovenants(["炎", "维多利亚", "突袭"]);

        var item = vm.MatchingLineups.Single();
        var transformed = item.Operators.Single(o => o.Name == "炎1");
        transformed.Sub.Should().Be("转→炎", "v1.6.0 keeps transformations in Equipments, not TransformedCovenant");
        transformed.ToolTip.Should().Contain("转职 → 炎");
        item.Operators.Single(o => o.Name == "炎2").Sub.Should().BeEmpty();
    }

    [Fact]
    public void MatchingLineupsFollowSavedLineupChangesAndSkipTeamsWithoutActivatedCovenants()
    {
        var (vm, _, lineups) = CreateSaved();
        vm.SetAvailableCovenants(["炎", "维多利亚", "突袭"]);

        lineups.LoadLineups([Saved("纯炎队", "炎1", "炎2", "炎3")]);
        vm.MatchingLineups.Should().ContainSingle().Which.Name.Should().Be("纯炎队");

        lineups.LoadLineups([Saved("未成队", "独1")]);
        vm.MatchingLineups.Should().BeEmpty("a team without activated covenants is not a match");
        vm.MatchingLineupHint.Should().Contain("还没有激活盟约");

        lineups.LoadLineups([]);
        vm.MatchingLineups.Should().BeEmpty();
        vm.MatchingLineupHint.Should().Contain("还没有保存过阵容");
    }

    [Fact]
    public void SelectingSavedLineupsTracksEveryChosenTeamInOneBatch()
    {
        var (vm, _, lineups) = CreateSaved();
        vm.SetAvailableCovenants(["炎", "维多利亚", "突袭"]);
        lineups.LoadLineups([
            Saved("双盟约队", "炎1", "炎2", "炎3", "独1", "独2"),
            Saved("纯炎队", "炎1", "炎2", "炎3"),
        ]);

        vm.SelectedMatchingLineupCount.Should().Be(0);
        vm.HasSelectedMatchingLineups.Should().BeFalse();
        vm.TrackSelectedText.Should().Be("追踪已选阵容");
        vm.SelectAllMatchingText.Should().Be("全选");

        vm.ToggleSelectAllMatchingCommand.Execute(null);
        vm.SelectedMatchingLineupCount.Should().Be(2);
        vm.HasSelectedMatchingLineups.Should().BeTrue();
        vm.TrackSelectedText.Should().Be("追踪已选阵容 (2)");
        vm.SelectAllMatchingText.Should().Be("取消全选");

        IReadOnlyList<Lineup>? tracked = null;
        vm.TrackSavedLineupsRequested += selected => tracked = selected;
        vm.TrackSelectedSavedLineupsCommand.Execute(null);
        tracked.Should().NotBeNull();
        tracked!.Select(l => l.Name).Should().BeEquivalentTo("双盟约队", "纯炎队");

        vm.MatchingLineups.Single(l => l.Name == "纯炎队").IsSelected = false;
        vm.SelectedMatchingLineupCount.Should().Be(1);
        vm.SelectAllMatchingText.Should().Be("全选");
    }

    [Fact]
    public void SavedLineupSelectionSurvivesBanRefreshAndHiddenTeamsAreNotTracked()
    {
        var (vm, bans, lineups) = CreateSaved();
        vm.SetAvailableCovenants(["炎", "维多利亚", "突袭"]);
        lineups.LoadLineups([Saved("纯炎队", "炎1", "炎2", "炎3")]);
        vm.MatchingLineups.Single().IsSelected = true;

        bans.SetCovenantBans(["炎"]);
        vm.MatchingLineups.Should().BeEmpty();
        vm.SelectedMatchingLineupCount.Should().Be(0);
        var tracked = new List<Lineup>();
        vm.TrackSavedLineupsRequested += selected => tracked.AddRange(selected);
        vm.TrackSelectedSavedLineupsCommand.Execute(null);
        tracked.Should().BeEmpty("only visible selected lineups may be tracked");

        bans.SetCovenantBans([]);
        vm.MatchingLineups.Single().IsSelected.Should().BeTrue("the selection is kept by lineup id");
        vm.SelectedMatchingLineupCount.Should().Be(1);
    }

    private static Lineup Saved(string name, params string[] operators) => new()
    {
        Name = name,
        Slots = operators.Select(operatorName => new LineupSlot { OperatorName = operatorName }).ToList(),
    };

    private static (LineupPlannerViewModel Vm, BanViewModel Bans, LineupViewModel Lineups) CreateSaved()
    {
        var operators = new List<Operator>
        {
            Op("炎1", "炎"), Op("炎2", "炎"), Op("炎3", "炎"),
            new() { Name = "独1", CoreCovenants = ["维多利亚"], AdditionalCovenants = ["突袭"] },
            new() { Name = "独2", CoreCovenants = ["维多利亚"], AdditionalCovenants = ["突袭"] },
        };
        var covenants = new[]
        {
            new CovenantInfo { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new CovenantInfo { Name = "维多利亚", BondType = "SEASON", ActivateCount = 3 },
            new CovenantInfo { Name = "突袭", BondType = "REGULAR", ActivateCount = 2 },
        };
        var bans = new BanViewModel();
        var lineups = new LineupViewModel(
            new LineupStore(Path.Combine(Path.GetTempPath(), $"planner_lineups_{Guid.NewGuid():N}"), EquipmentTestData.Rules),
            new CovenantCalculator(operators, covenants,
                new Dictionary<string, string>(EquipmentTestData.Rules.EquipmentToCovenant)));
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, bans, EquipmentTestData.Rules, lineups)
        {
            DebounceMilliseconds = 0,
        };
        return (vm, bans, lineups);
    }
}
