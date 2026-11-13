using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Data;

/// <summary>
/// 阵容文件持久化：每个阵容一个 &lt;id&gt;.json 文件，存于
/// %AppData%\ASPAssistant\lineups\，避免单文件并发风险。
/// </summary>
public class LineupStore
{
    private readonly string _dir;
    private readonly LineupEquipmentService _equipmentRules;
    private readonly SemaphoreSlim _orderLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public LineupStore(string lineupsDir, LineupEquipmentService equipmentRules)
    {
        _dir = lineupsDir;
        _equipmentRules = equipmentRules;
    }

    public async Task<List<Lineup>> LoadAllAsync()
    {
        if (!Directory.Exists(_dir))
            return [];

        var files = Directory.EnumerateFiles(_dir, "*.json");
        var result = new List<Lineup>();
        foreach (var file in files)
        {
            Lineup? lineup;
            try
            {
                var json = await File.ReadAllTextAsync(file);
                lineup = JsonSerializer.Deserialize<Lineup>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                AppLogger.Warn("LineupStore", $"Failed to load {file}: {ex.Message}");
                continue;
            }
            if (lineup is null) continue;
            if (string.IsNullOrEmpty(lineup.Id)) lineup.Id = Path.GetFileNameWithoutExtension(file);
            if (_equipmentRules.Upgrade(lineup))
            {
                File.Copy(file, file + $".pre-equipment-{Guid.NewGuid():N}.bak");
                await WriteAsync(file, lineup);
            }
            result.Add(lineup);
        }
        List<string> order;
        try
        {
            order = JsonSerializer.Deserialize<List<string>>(
                await File.ReadAllTextAsync(Path.Combine(_dir, "order", "lineups.json")))
                ?? throw new JsonException("阵容顺序不能为空");
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Existing profiles have no manual order until the first move or new lineup.
            return [.. result.OrderByDescending(l => l.UpdatedAt)];
        }
        var ranks = order.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        return [.. result.OrderBy(l => ranks.TryGetValue(l.Id, out var index) ? index : int.MaxValue)
            .ThenByDescending(l => l.UpdatedAt)];
    }

    public async Task SaveOrderAsync(IEnumerable<string> ids)
    {
        var json = JsonSerializer.Serialize(ids, JsonOptions);
        await _orderLock.WaitAsync();
        var directory = Path.GetFullPath(Path.Combine(_dir, "order"));
        var path = Path.Combine(directory, "lineups.json");
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(directory);
            try
            {
                await File.WriteAllTextAsync(temporary, json);
                try { File.Copy(path, path + ".bak", true); }
                catch (FileNotFoundException) { /* First saved order has no previous version. */ }
                File.Move(temporary, path, true);
            }
            finally { File.Delete(temporary); }
        }
        finally { _orderLock.Release(); }
    }

    public async Task SaveAsync(Lineup lineup)
    {
        Directory.CreateDirectory(_dir);
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        lineup.UpdatedAt = DateTime.UtcNow;

        var path = Path.GetFullPath(Path.Combine(_dir, $"{lineup.Id}.json"));
        if (Path.GetDirectoryName(path) != Path.GetFullPath(_dir))
            throw new InvalidDataException("阵容 ID 不能包含目录路径");
        await WriteAsync(path, lineup);
    }

    private static async Task WriteAsync(string path, Lineup lineup)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(lineup, JsonOptions));
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    public Task DeleteAsync(string id)
    {
        var path = Path.Combine(_dir, $"{id}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }
}
