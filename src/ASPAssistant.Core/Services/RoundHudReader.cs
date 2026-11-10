// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

/// <summary>
/// 一帧游戏画面的回合 HUD 读数。<see cref="Round"/> 为空表示这一帧没读到回合数
/// （例如不在对局中、画面被遮挡、或只读到了「已满级」）。
/// </summary>
public readonly record struct RoundHud(int? Round, bool UpgradeMaxed, bool UpgradeAvailable)
{
    public bool Detected => Round is > 0;
}

/// <summary>
/// 读取游戏对局左上角的回合数，以及左下角「升级 / 已满级」按钮状态。
///
/// 两个区域与 <see cref="InformationScreenDetector.IsGameplayAsync"/> 使用的百分比完全一致，
/// 因此任何能通过「是否在对局中」判定的画面，这里都能直接读到数字——
/// 之前的实现只判断区域里有没有「回合」两个字，把数字丢掉了。
/// </summary>
public sealed class RoundHudReader
{
    // 回合窄带：与 InformationScreenDetector.IsGameplayAsync 相同（1920x1080 下为 x 652..901）。
    private const double RoundXPercent = 0.34;
    private const double RoundYPercent = 0.00;
    private const double RoundWPercent = 0.13;
    private const double RoundHPercent = 0.10;

    // 左下角升本按钮：同一套百分比。
    private const double UpgradeXPercent = 0.10;
    private const double UpgradeYPercent = 0.88;
    private const double UpgradeWPercent = 0.35;
    private const double UpgradeHPercent = 0.09;

    private static readonly Regex InlineRound =
        new(@"(?<n>\d{1,2})\s*回合|回合\D{0,2}(?<n>\d{1,2})", RegexOptions.Compiled);

    private readonly IOcrEngine _ocr;

    public RoundHudReader(IOcrEngine ocr) => _ocr = ocr;

    /// <summary>
    /// 一次性读取回合数与升本按钮状态。任意一段识别失败都只是返回空读数，不抛异常。
    /// </summary>
    public async Task<RoundHud> ReadAsync(byte[] screenshot, int width, int height)
    {
        if (screenshot.Length == 0 || width <= 0 || height <= 0) return default;

        var roundTexts = await _ocr.RecognizeRegionAsync(screenshot,
            (int)(width * RoundXPercent), (int)(height * RoundYPercent),
            (int)(width * RoundWPercent), (int)(height * RoundHPercent));

        var upgradeTexts = await _ocr.RecognizeRegionAsync(screenshot,
            (int)(width * UpgradeXPercent), (int)(height * UpgradeYPercent),
            (int)(width * UpgradeWPercent), (int)(height * UpgradeHPercent));

        var maxed = false;
        var available = false;
        foreach (var text in upgradeTexts)
        {
            var normalized = Normalize(text.Text);
            if (normalized.Contains("已满级")) maxed = true;
            else if (normalized.Contains("升级")) available = true;
        }

        return new RoundHud(ParseRound(roundTexts), maxed, available);
    }

    /// <summary>
    /// 从回合窄带的 OCR 结果里解析出回合数。
    ///
    /// 实测 MaaOCR 会把「回合 12」拆成两个 segment（"回合" box=(700,32,39,25)、
    /// "12" box=(747,30,41,26)），也可能合成一个（"回合12"），所以先按合写的模式找，
    /// 找不到再取标签右侧最近的纯数字 segment。
    /// </summary>
    public static int? ParseRound(IReadOnlyList<OcrTextResult> texts)
    {
        if (texts.Count == 0) return null;

        foreach (var text in texts)
        {
            var match = InlineRound.Match(Normalize(text.Text));
            if (match.Success && TryParseRound(match.Groups["n"].Value, out var inline))
                return inline;
        }

        OcrTextResult? label = null;
        foreach (var text in texts)
        {
            if (Normalize(text.Text).Contains("回合"))
            {
                label = text;
                break;
            }
        }
        if (label is not { } labelBox) return null;

        var labelRight = labelBox.Box.X + labelBox.Box.W;
        int? best = null;
        var bestDistance = int.MaxValue;
        foreach (var text in texts)
        {
            var candidate = Normalize(text.Text).Trim();
            if (candidate.Length is 0 or > 2) continue;
            if (!candidate.All(char.IsAsciiDigit)) continue;
            if (!TryParseRound(candidate, out var value)) continue;
            var distance = Math.Abs(text.Box.X - labelRight);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = value;
        }
        return best;
    }

    private static bool TryParseRound(string raw, out int round)
    {
        round = 0;
        if (!int.TryParse(raw, out var value)) return false;
        if (value < UpgradeRoundLowest || value > UpgradeRoundHighest) return false;
        round = value;
        return true;
    }

    // 回合数的合理范围：1 起步，上限比 UpgradeRoundPlan.MaxRound 略宽松，
    // 只用来过滤明显的误识别（例如把「12」读成「112」）。
    private const int UpgradeRoundLowest = 1;
    private const int UpgradeRoundHighest = 99;

    /// <summary>全角数字、空格与常见 OCR 噪声的归一化。</summary>
    private static string Normalize(string text)
    {
        Span<char> buffer = stackalloc char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == ' ') { buffer[i] = '\0'; continue; }
            if (c is >= '０' and <= '９') { buffer[i] = (char)('0' + (c - '０')); continue; }
            buffer[i] = c;
        }
        return new string(buffer).Replace("\0", "");
    }
}
