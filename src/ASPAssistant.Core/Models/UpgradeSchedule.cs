// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>
/// 一局里的一个回合区间，闭区间：<c>From &lt;= 回合 &lt;= To</c>。
/// 升本不是「某一回合必须点」，而是「这几个回合里找机会升上去」，所以计划用区间表示。
/// </summary>
public readonly record struct RoundWindow(int From, int To)
{
    public int Count => To - From + 1;

    public bool Contains(int round) => round >= From && round <= To;

    /// <summary>「5–7」这样的短区间；起止相同时只写一个数。</summary>
    public string Short => From == To ? From.ToString() : $"{From}–{To}";

    /// <summary>「第 5–7 回合」。</summary>
    public string Describe() => $"第 {Short} 回合";
}

/// <summary>
/// 阵容的「升本计划」：一局 1 到 <see cref="LevelCount"/> 本。
/// 1 本是进入对局时的初始等级，不需要计划；2–6 本每一本记一个回合区间（<see cref="RoundWindow"/>）。
/// </summary>
public sealed class UpgradeSchedule
{
    /// <summary>卫戍协议一局的本数上限：1 本到 6 本。</summary>
    public const int LevelCount = 6;

    /// <summary>第一本：进入对局就有的等级，永远不需要「升」。</summary>
    public const int FirstLevel = 1;

    /// <summary>最早可能升本的回合。</summary>
    public const int MinRound = 1;

    /// <summary>最晚可能升本的回合；超过这个值一定是识别或输入错误。</summary>
    public const int MaxRound = 30;

    private readonly RoundWindow?[] _windows;

    private UpgradeSchedule(RoundWindow?[] windows) => _windows = windows;

    /// <summary>什么都没设的计划。</summary>
    public static UpgradeSchedule Empty { get; } = new(new RoundWindow?[LevelCount]);

    /// <summary>
    /// 用「每本的起止回合」两个数组构造计划（索引 0 对应 1 本，索引 5 对应 6 本）。
    /// 两端都合法才成立：越界丢弃、起点大于终点自动对调；1 本视为开局，任何输入都会被忽略。
    /// </summary>
    public static UpgradeSchedule FromArrays(int?[]? from, int?[]? to)
    {
        var windows = new RoundWindow?[LevelCount];
        for (var i = FirstLevel; i < LevelCount; i++)
        {
            var start = Read(from, i);
            var end = Read(to, i);
            if (start is null || end is null) continue;

            windows[i] = start.Value <= end.Value
                ? new RoundWindow(start.Value, end.Value)
                : new RoundWindow(end.Value, start.Value);
        }

        return new UpgradeSchedule(windows);
    }

    /// <summary>兼容旧接口：只给一组回合时，把每一本当成起止相同的单回合区间。</summary>
    public static UpgradeSchedule FromArray(int?[]? source) => FromArrays(source, source);

    private static int? Read(int?[]? source, int index)
    {
        if (source is null || index >= source.Length) return null;
        return source[index] is >= MinRound and <= MaxRound ? source[index] : null;
    }

    /// <summary>2–6 本都没设区间。</summary>
    public bool IsEmpty
    {
        get
        {
            for (var i = FirstLevel; i < LevelCount; i++)
            {
                if (_windows[i] is not null) return false;
            }
            return true;
        }
    }

    /// <summary>第 <paramref name="level"/> 本（2–6）的计划区间；1 本、没设置或越界时为 null。</summary>
    public RoundWindow? WindowForLevel(int level)
        => level > FirstLevel && level <= LevelCount ? _windows[level - 1] : null;

    /// <summary>这一回合落在哪几本的区间里（按本数升序）；空表示这一回合不需要提醒。</summary>
    public IReadOnlyList<int> LevelsAt(int round)
    {
        if (round < MinRound || round > MaxRound) return [];

        List<int> levels = [];
        for (var i = FirstLevel; i < LevelCount; i++)
        {
            if (_windows[i]?.Contains(round) == true) levels.Add(i + 1);
        }
        return levels;
    }

    /// <summary>落盘用的起点数组副本（索引 0 = 1 本，永远是 null）。</summary>
    public int?[] ToFromArray() => Project(fromStart: true);

    /// <summary>落盘用的终点数组副本（索引 0 = 1 本，永远是 null）。</summary>
    public int?[] ToToArray() => Project(fromStart: false);

    private int?[] Project(bool fromStart)
    {
        var result = new int?[LevelCount];
        for (var i = 0; i < LevelCount; i++)
        {
            if (_windows[i] is not { } window) continue;
            result[i] = fromStart ? window.From : window.To;
        }
        return result;
    }

    /// <summary>「2本 3–4 · 3本 5–7」这样的短描述；没设置时返回「未设置」。</summary>
    public string Describe()
    {
        List<string> parts = [];
        for (var i = FirstLevel; i < LevelCount; i++)
        {
            if (_windows[i] is { } window) parts.Add($"{i + 1}本 {window.Short}");
        }
        return parts.Count == 0 ? "未设置" : string.Join(" · ", parts);
    }

    /// <summary>逐本的说明行，给 UI 列表用；没设置的本不出现。</summary>
    public IReadOnlyList<string> DescribeLines()
    {
        List<string> lines = [];
        for (var i = FirstLevel; i < LevelCount; i++)
        {
            if (_windows[i] is { } window) lines.Add($"{i + 1} 本 · {window.Describe()}");
        }
        return lines;
    }

    /// <summary>
    /// 本数顺序上是否出现「后面的本比前面的本还早」这种明显填反的情况。
    /// 只比较起点，所以相邻两本的区间重叠（比如 2本 4–8、3本 5–9）是允许的。
    /// </summary>
    public bool HasOrderingIssue
    {
        get
        {
            var previousStart = 0;
            for (var i = FirstLevel; i < LevelCount; i++)
            {
                if (_windows[i] is not { } window) continue;
                if (window.From < previousStart) return true;
                previousStart = window.From;
            }
            return false;
        }
    }
}
