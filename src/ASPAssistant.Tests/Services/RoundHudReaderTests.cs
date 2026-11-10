// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class RoundHudReaderTests
{
    private static OcrTextResult T(string text, int x, int y, int w, int h) => new(text, (x, y, w, h));

    [Fact]
    public void ParseRound_ReadsTheNumberFromTheSeparateSegmentMaaActuallyReturns()
    {
        // 实测（gameplay-markers-1920x1080.png）：「回合」与「12」是两个 segment。
        OcrTextResult[] texts = [T("回合", 700, 32, 39, 25), T("12", 747, 30, 41, 26)];
        RoundHudReader.ParseRound(texts).Should().Be(12);
    }

    [Fact]
    public void ParseRound_ReadsNumberRightOfTheLabelWhenOtherDigitsArePresent()
    {
        OcrTextResult[] texts =
        [
            T("回合", 700, 32, 39, 25),
            T("99", 10, 40, 30, 25),   // 远处的干扰数字
            T("7", 745, 30, 25, 26),
        ];
        RoundHudReader.ParseRound(texts).Should().Be(7);
    }

    [Theory]
    [InlineData("回合12")]
    [InlineData("回合 12")]
    [InlineData("12回合")]
    [InlineData("回合：12")]
    public void ParseRound_HandlesInlineFormats(string text)
    {
        RoundHudReader.ParseRound([T(text, 700, 30, 120, 30)]).Should().Be(12);
    }

    [Fact]
    public void ParseRound_NormalizesFullWidthDigits()
    {
        RoundHudReader.ParseRound([T("回合１２", 700, 30, 120, 30)]).Should().Be(12);
    }

    [Fact]
    public void ParseRound_ReturnsNullWithoutRoundCue()
    {
        RoundHudReader.ParseRound([]).Should().BeNull();
        RoundHudReader.ParseRound([T("确认本局信息", 500, 30, 200, 30)]).Should().BeNull();
        // 有标签但附近没有数字
        RoundHudReader.ParseRound([T("回合", 700, 32, 39, 25)]).Should().BeNull();
    }

    [Fact]
    public void ParseRound_RejectsImplausibleNumbers()
    {
        // 0 与 3 位数都不是合法回合，避免把噪声当回合数。
        RoundHudReader.ParseRound([T("回合", 700, 32, 39, 25), T("0", 747, 30, 41, 26)]).Should().BeNull();
        RoundHudReader.ParseRound([T("回合", 700, 32, 39, 25), T("112", 747, 30, 41, 26)]).Should().BeNull();
    }

    [Fact]
    public void RoundHud_DetectedOnlyWithPositiveRound()
    {
        new RoundHud(null, false, false).Detected.Should().BeFalse();
        new RoundHud(0, false, false).Detected.Should().BeFalse();
        new RoundHud(1, false, true).Detected.Should().BeTrue();
    }
}
