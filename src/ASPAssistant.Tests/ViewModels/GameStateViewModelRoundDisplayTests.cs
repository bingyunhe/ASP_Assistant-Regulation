// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class GameStateViewModelRoundDisplayTests
{
    [Fact]
    public void RoundDisplay_IsReadableBeforeAndAfterRecognition()
    {
        var vm = new GameStateViewModel();
        vm.RoundDisplay.Should().Be("未识别");
        vm.UpgradeStateDisplay.Should().Be("未识别");

        vm.CurrentRound = 9;
        vm.RoundDisplay.Should().Be("第 9 回合");
        vm.UpgradeStateDisplay.Should().Be("可升级");

        vm.UpgradeMaxed = true;
        vm.UpgradeStateDisplay.Should().Be("已满级");
    }

    [Fact]
    public void RoundDisplay_NotifiesWhenRoundChanges()
    {
        var vm = new GameStateViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.CurrentRound = 4;

        raised.Should().Contain(nameof(GameStateViewModel.RoundDisplay))
            .And.Contain(nameof(GameStateViewModel.UpgradeStateDisplay));
    }

    [Fact]
    public void MaxedStateWithoutRoundStillExplainsItself()
    {
        var vm = new GameStateViewModel { UpgradeMaxed = true };
        // 已经满级时回合读不读得到都不影响按钮状态。
        vm.UpgradeStateDisplay.Should().Be("已满级");
    }
}
