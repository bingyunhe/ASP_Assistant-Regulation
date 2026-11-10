// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Services;
using FluentAssertions;
using Xunit.Abstractions;

namespace ASPAssistant.Tests.Services;

/// <summary>
/// 用仓库自带的 PaddleOCR 模型跑真实截图的回合读数，确保「回合 12」这类
/// 数字能从游戏画面里稳定读出来（而不只是单元测试里的合成 segment）。
/// </summary>
[Collection("Native OCR")]
public class RoundHudNativeTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
    : IClassFixture<EnemyRecognitionFixture>
{
    [Fact]
    public async Task GameplayScreenshotYieldsRoundTwelveAndMaxedUpgradeButton()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData",
            "gameplay-markers-1920x1080.png"));

        var hud = await new RoundHudReader(fixture.Engine).ReadAsync(bytes, 1920, 1080);
        output.WriteLine($"round={hud.Round} maxed={hud.UpgradeMaxed} available={hud.UpgradeAvailable}");

        hud.Detected.Should().BeTrue();
        hud.Round.Should().Be(12);
        hud.UpgradeMaxed.Should().BeTrue();
    }

    [Fact]
    public async Task OpeningScreenshotYieldsNoRound()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData",
            "ban-sample-1920x1080.png"));

        var hud = await new RoundHudReader(fixture.Engine).ReadAsync(bytes, 1920, 1080);
        output.WriteLine($"round={hud.Round} maxed={hud.UpgradeMaxed} available={hud.UpgradeAvailable}");

        hud.Detected.Should().BeFalse();
    }
}
