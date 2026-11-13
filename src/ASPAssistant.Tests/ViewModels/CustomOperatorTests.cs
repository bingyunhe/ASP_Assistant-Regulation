using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class CustomOperatorTests
{
    [Fact]
    public void PlannersAndExtendedPoolOnlyUseSelectedCustomOperators()
    {
        var fixedOp = new Operator { Name = "默认", CoreCovenants = ["炎"] };
        var custom = new Operator { Name = "自选", IsCustom = true, CoreCovenants = ["炎"], Tier = 5 };
        var operators = new[] { fixedOp, custom };
        var planner = new LineupPlanner(operators, [new() { Name = "炎", BondType = "SEASON", ActivateCount = 1 }]);
        var request = new PlannerRequest { Goals = [new("炎", 1, 1, false, true)], PeopleOptions = [1], TransformOptions = [0] };
        planner.Plan(request).CandidateCount.Should().Be(1);
        var tracked = new[] { new TrackingEntry { Name = fixedOp.Name } };
        DeployPoolBuilder.Build(tracked, operators.ToDictionary(o => o.Name), new DeployPoolState())
            .SelectMany(g => g.Items).Should().NotContain(i => i.Name == custom.Name);
        custom.IsSelectedCustom = true;
        planner.Plan(request).CandidateCount.Should().Be(2);
        DeployPoolBuilder.Build(tracked, operators.ToDictionary(o => o.Name), new DeployPoolState())
            .SelectMany(g => g.Items).Should().Contain(i => i.Name == custom.Name);
    }

    [Fact]
    public async Task PrtsCatalogueIsUniqueSeparateFromDefaultsAndHasPortraitsAndProfiles()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var defaults = await store.LoadOperatorsAsync();
        var custom = await store.LoadCustomOperatorsAsync();
        custom.Should().HaveCountGreaterThan(12);
        custom.Select(o => o.Name).Should().OnlyHaveUniqueItems();
        custom.Select(o => o.Name).Intersect(defaults.Select(o => o.Name)).Should().BeEmpty();
        custom.Should().OnlyContain(o => o.IsCustom && !o.IsAvailableForSelection && o.Tier == 5
            && o.Normal.Traits.Count == 0 && o.Elite.Traits.Count == 0 && o.Profile != null
            && o.CoreCovenants.Count + o.AdditionalCovenants.Count > 0);
        foreach (var op in custom)
        {
            var image = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "data", op.IconPath));
            image.Take(8).Should().Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        }
    }

    [Fact]
    public void SourceSwitchesOnlyShowUserSelectedCustomOperatorsAndKeepBanFiltering()
    {
        var selected = new Operator { Name = "自选甲", IsCustom = true, IsSelectedCustom = true, Tier = 5, CoreCovenants = ["炎"] };
        var unselected = new Operator { Name = "自选乙", IsCustom = true, Tier = 5, CoreCovenants = ["炎"] };
        var fixedOp = new Operator { Name = "默认", Tier = 1, CoreCovenants = ["炎"] };
        var vm = new OperatorBrowseViewModel { SelectedOperatorSource = OperatorSourceFilter.Default };
        vm.LoadOperators([fixedOp, selected, unselected]);
        vm.FilteredOperators.Should().Equal(fixedOp);
        vm.SelectedOperatorSource = OperatorSourceFilter.Custom;
        vm.FilteredOperators.Should().Equal(selected);
        vm.CoreChips.Single(c => c.Value == "炎").Count.Should().Be(1);
        vm.SetBanChecker(name => name == selected.Name);
        vm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
        vm.FilteredOperators.Should().BeEmpty();
        vm.SelectedOperatorSource = OperatorSourceFilter.All;
        vm.FilteredOperators.Should().Equal(fixedOp, selected);
        unselected.IsSelectedCustom = true;
        vm.LoadOperators([fixedOp, selected, unselected]);
        vm.CustomSelectionCount.Should().Be(2);
        vm.FilteredOperators.Should().Equal(fixedOp, selected, unselected);
    }

    [Fact]
    public async Task TwelveSelectionsRoundTripIndependentlyAndRetainABackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aspa-custom-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsManager(directory);
        try
        {
            (await settings.LoadCustomOperatorSelectionAsync()).Should().BeEmpty();
            var names = Enumerable.Range(1, 12).Select(i => "干员" + i).ToList();
            await settings.SaveCustomOperatorSelectionAsync(names);
            (await settings.LoadCustomOperatorSelectionAsync()).Should().Equal(names);
            await settings.SaveCustomOperatorSelectionAsync([]);
            (await settings.LoadCustomOperatorSelectionAsync()).Should().BeEmpty();
            JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(Path.Combine(directory, "custom-operators.json.bak")))
                .Should().Equal(names);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[\"a\",\"a\"]")]
    [InlineData("[\"a\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\",\"h\",\"i\",\"j\",\"k\",\"l\",\"m\"]")]
    public async Task InvalidSelectionFilesAreRejectedAtTheReadBoundary(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aspa-custom-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "custom-operators.json"), json);
            await Assert.ThrowsAsync<JsonException>(() => new SettingsManager(directory).LoadCustomOperatorSelectionAsync());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
