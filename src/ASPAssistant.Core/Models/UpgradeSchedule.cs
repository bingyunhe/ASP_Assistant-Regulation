// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>
/// 阵容的「升本计划」：一共 <see cref="LevelCount"/> 本，每一本记一个计划回合（1–30）。
/// 索引 0 对应「升到 1 本」，索引 5 对应「升到 6 本」；null 表示这一本不设提醒。
/// </summary>
public sealed class UpgradeSchedule
{
    /// <summary>卫戍协议一局的本数上限：1 本到 6 本。</summary>
    public const int LevelCount = 6;

    /// <summary>一局最早可能出现升本的回合。</summary>
    public const int MinRound = 1;

    /// <summary>一局最晚可能出现升本的回合；超过这个值一定是识别或输入错误。</summary>
    public const int MaxRound = 30;

    private readonly int?[] _rounds;

    private UpgradeSchedule(int?[] rounds) => _rounds = rounds;

    /// <summary>什么都没设的计划。</summary>
    public static UpgradeSchedule Empty { get; } = new(new int?[LevelCount]);

    /// <summary>
    /// 把阵容文件里的数组规整成固定长度：长度不足补 null，
    /// 越界或非 1–30 的值当作未设置丢掉，避免手改存档导致提醒乱弹。
    /// </summary>
    public static UpgradeSchedule FromArray(int?[]? source)
    {
        if (source is null || source.Length == 0) return Empty;

        var rounds = new int?[LevelCount];
        for (var i = 0; i < LevelCount && i < source.Length; i++)
            rounds[i] = source[i] is >= MinRound and <= MaxRound ? source[i] : null;
        return new UpgradeSchedule(rounds);
    }

    /// <summary>6 本都没设回合。</summary>
    public bool IsEmpty => Array.TrueForAll(_rounds, round => round is null);

    /// <summary>第 <paramref name="level"/> 本（1–6）的计划回合；没设置或本数越界时为 null。</summary>
    public int? RoundForLevel(int level)
        => level >= 1 && level <= LevelCount ? _rounds[level - 1] : null;

    /// <summary>这一回合该升到哪几本（按本数升序）；空表示这一回合不需要提醒。</summary>
    public IReadOnlyList<int> LevelsAt(int round)
    {
        if (round < MinRound || round > MaxRound) return [];

        List<int> levels = [];
        for (var i = 0; i < LevelCount; i++)
        {
            if (_rounds[i] == round) levels.Add(i + 1);
        }
        return levels;
    }

    /// <summary>落盘用的数组副本。</summary>
    public int?[] ToArray() => (int?[])_rounds.Clone();

    /// <summary>「2本 第4 · 3本 第8」这样的短描述；没设置时返回「未设置」。</summary>
    public string Describe()
    {
        List<string> parts = [];
        for (var i = 0; i < LevelCount; i++)
        {
            if (_rounds[i] is { } round) parts.Add($"{i + 1}本 第{round}");
        }
        return parts.Count == 0 ? "未设置" : string.Join(" · ", parts);
    }
}
