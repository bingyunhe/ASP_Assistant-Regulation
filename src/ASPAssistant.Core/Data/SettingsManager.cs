// Modified for ASP Assistant Regulation on 2026-11-11; see NOTICE.
using System.Text.Json;
using System.Text.Json.Serialization;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Data;

public class SettingsManager
{
    private readonly string _settingsDir;
    private readonly SemaphoreSlim _sidePanelPositionLock = new(1, 1);
    private readonly SemaphoreSlim _trackingLock = new(1, 1);
    private readonly SemaphoreSlim _deployPoolLock = new(1, 1);
    private readonly SemaphoreSlim _gameConnectionLock = new(1, 1);
    private readonly SemaphoreSlim _customSelectionLock = new(1, 1);
    private static readonly JsonSerializerOptions ThemeJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter<GameShopTheme>(allowIntegerValues: false) }
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsManager(string settingsDir)
    {
        _settingsDir = settingsDir;
    }

    public async Task<GameConnectionSettings> LoadGameConnectionAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(Path.Combine(_settingsDir, "game-connection.json"));
            var settings = JsonSerializer.Deserialize<GameConnectionSettings>(json, JsonOptions);
            if (settings is null || !Enum.IsDefined(settings.Source)
                || settings.AdbPath is null || settings.AdbSerial is null || settings.WindowTitle is null)
                throw new JsonException("游戏连接设置无效");
            return settings;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            AppLogger.Warn("Settings", $"Using PC capture settings: {ex.Message}");
            return new();
        }
    }

    public async Task SaveGameConnectionAsync(GameConnectionSettings settings)
    {
        await _gameConnectionLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var path = Path.Combine(_settingsDir, "game-connection.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        finally { _gameConnectionLock.Release(); }
    }

    public async Task<bool> LoadLightThemeAsync()
    {
        var path = Path.Combine(_settingsDir, "light-theme.json");
        if (!File.Exists(path)) return false;
        return JsonSerializer.Deserialize<bool>(await File.ReadAllTextAsync(path));
    }

    public async Task<List<string>> LoadCustomOperatorSelectionAsync()
    {
        string json;
        try { json = await File.ReadAllTextAsync(Path.Combine(_settingsDir, "custom-operators.json")); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return []; }
        var names = JsonSerializer.Deserialize<List<string>>(json)
            ?? throw new JsonException("自选干员配置不能为空");
        if (names.Count > 12 || names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            throw new JsonException("自选干员配置必须为不重复的最多 12 名干员");
        return names;
    }

    public async Task SaveCustomOperatorSelectionAsync(IReadOnlyList<string> names)
    {
        var json = JsonSerializer.Serialize(names, JsonOptions);
        await _customSelectionLock.WaitAsync();
        var path = Path.GetFullPath(Path.Combine(_settingsDir, "custom-operators.json"));
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(_settingsDir);
            try
            {
                await File.WriteAllTextAsync(temporary, json);
                try { File.Copy(path, path + ".bak", true); }
                catch (FileNotFoundException) { /* No earlier selection on first save. */ }
                File.Move(temporary, path, true);
            }
            finally { File.Delete(temporary); }
        }
        finally { _customSelectionLock.Release(); }
    }

    public async Task SaveLightThemeAsync(bool isLight)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "light-theme.json"),
            JsonSerializer.Serialize(isLight));
    }

    /// <summary>
    /// Side panel theme + day/night. Older installs only have light-theme.json: its value becomes the night
    /// choice of 终端 (the former only theme).
    /// </summary>
    public async Task<SidebarAppearance> LoadSidebarAppearanceAsync()
    {
        var path = Path.Combine(_settingsDir, "appearance.json");
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<SidebarAppearance>(await File.ReadAllTextAsync(path), ThemeJsonOptions) ?? new();
        }
        catch (JsonException)
        {
            AppLogger.Warn("Settings", "侧栏外观配置无法解析，使用默认值");
            return new SidebarAppearance();
        }
        // Older installs followed the 模式主题 with 终端 at night unless light-theme.json said otherwise.
        if (File.Exists(Path.Combine(_settingsDir, "light-theme.json")))
            return new SidebarAppearance
            {
                FollowModeTheme = true,
                Night = { [GameShopTheme.Terminal] = !await LoadLightThemeAsync() }
            };
        return new SidebarAppearance();
    }

    private sealed record SavedTerminalLevel(int Level, DateTime SavedAtUtc);

    /// <summary>Terminal level of the current run, kept so restarting the app mid-run does not lose it.</summary>
    public static readonly TimeSpan TerminalLevelMaxAge = TimeSpan.FromHours(2);

    /// <summary>The saved level, or null when there is none or it is older than <see cref="TerminalLevelMaxAge"/>.</summary>
    public async Task<int?> LoadTerminalLevelAsync(DateTime? nowUtc = null)
    {
        var path = Path.Combine(_settingsDir, "terminal-level.json");
        try
        {
            if (!File.Exists(path)) return null;
            var saved = JsonSerializer.Deserialize<SavedTerminalLevel>(await File.ReadAllTextAsync(path));
            if (saved is null || saved.Level is < 1 or > 6) return null;
            return (nowUtc ?? DateTime.UtcNow) - saved.SavedAtUtc <= TerminalLevelMaxAge ? saved.Level : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Saves the run's level; 0 clears it (new run).</summary>
    public async Task SaveTerminalLevelAsync(int level, DateTime? nowUtc = null)
    {
        var path = Path.Combine(_settingsDir, "terminal-level.json");
        try
        {
            if (level <= 0) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(_settingsDir);
            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(new SavedTerminalLevel(level, nowUtc ?? DateTime.UtcNow)));
        }
        catch (IOException ex)
        {
            AppLogger.Warn("Settings", $"终端等级保存失败：{ex.Message}");
        }
    }

    public async Task SaveSidebarAppearanceAsync(SidebarAppearance appearance)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "appearance.json"),
            JsonSerializer.Serialize(appearance, ThemeJsonOptions));
    }

    public async Task<GameShopTheme?> LoadGameShopThemeAsync()
    {
        var path = Path.Combine(_settingsDir, "game-theme.json");
        try
        {
            var theme = JsonSerializer.Deserialize<GameShopTheme>(await File.ReadAllTextAsync(path), ThemeJsonOptions);
            if (Enum.IsDefined(theme)) return theme;
            AppLogger.Warn("Settings", "模式主题值无效，启动时重新选择");
            return null;
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            AppLogger.Warn("Settings", "模式主题配置无法解析，启动时重新选择");
            return null;
        }
    }

    public async Task<bool> LoadAutomaticGameThemeAsync()
    {
        try
        {
            return JsonSerializer.Deserialize<bool>(await File.ReadAllTextAsync(Path.Combine(_settingsDir, "game-theme-auto.json")));
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public async Task SaveAutomaticGameThemeAsync(bool automatic)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "game-theme-auto.json"), JsonSerializer.Serialize(automatic));
    }

    public async Task SaveGameShopThemeAsync(GameShopTheme theme)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "game-theme.json"),
            JsonSerializer.Serialize(theme, ThemeJsonOptions));
    }

    public async Task<bool> LoadTrackMultipleLineupsAsync()
    {
        var path = Path.Combine(_settingsDir, "multiple-lineups.json");
        if (!File.Exists(path)) return false;
        return JsonSerializer.Deserialize<bool>(await File.ReadAllTextAsync(path));
    }

    public async Task SaveTrackMultipleLineupsAsync(bool enabled)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "multiple-lineups.json"),
            JsonSerializer.Serialize(enabled));
    }

    /// <summary>Draw deployable cells and enemy routes over the game board (on by default).</summary>
    public async Task<bool> LoadShowBoardOverlayAsync()
    {
        var path = Path.Combine(_settingsDir, "board-overlay.json");
        if (!File.Exists(path)) return true;
        try { return JsonSerializer.Deserialize<bool>(await File.ReadAllTextAsync(path)); }
        catch (JsonException) { return true; }
    }

    public async Task SaveShowBoardOverlayAsync(bool enabled)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "board-overlay.json"), JsonSerializer.Serialize(enabled));
    }

    public async Task<DeployPoolState> LoadDeployPoolAsync()
    {
        var path = Path.Combine(_settingsDir, "deploy-pool.json");
        if (!File.Exists(path)) return new DeployPoolState();
        try { return JsonSerializer.Deserialize<DeployPoolState>(await File.ReadAllTextAsync(path), JsonOptions) ?? new(); }
        catch (JsonException) { return new DeployPoolState(); }
    }

    public async Task SaveDeployPoolAsync(DeployPoolState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        Directory.CreateDirectory(_settingsDir);
        var path = Path.Combine(_settingsDir, "deploy-pool.json");
        await _deployPoolLock.WaitAsync();
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            try { File.Copy(path, path + ".bak", true); }
            catch (FileNotFoundException) { }
            await File.WriteAllTextAsync(temporary, json);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); _deployPoolLock.Release(); }
    }

    public async Task<AssistantHotkey?> LoadAssistantHotkeyAsync()
    {
        string json;
        try { json = await File.ReadAllTextAsync(Path.Combine(_settingsDir, "assistant-hotkey.json")); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
        var hotkey = JsonSerializer.Deserialize<AssistantHotkey?>(json, JsonOptions);
        if (hotkey?.ConflictReason() is { } reason) throw new JsonException(reason);
        return hotkey;
    }

    public async Task SaveAssistantHotkeyAsync(AssistantHotkey? hotkey)
    {
        Directory.CreateDirectory(_settingsDir);
        var path = Path.Combine(_settingsDir, "assistant-hotkey.json");
        try
        {
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(hotkey, JsonOptions));
            try { File.Copy(path, path + ".bak", true); }
            catch (FileNotFoundException) { }
            File.Move(path + ".tmp", path, true);
        }
        finally { File.Delete(path + ".tmp"); }
    }

    public async Task<SidePanelPositionSettings> LoadSidePanelPositionAsync()
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(Path.Combine(_settingsDir, "side-panel-position.json"));
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new SidePanelPositionSettings();
        }
        var settings = JsonSerializer.Deserialize<SidePanelPositionSettings>(json, JsonOptions)
            ?? throw new JsonException("侧边栏位置配置不能为空");
        if (settings.Placement is { } placement && (!double.IsFinite(placement.Left) || !double.IsFinite(placement.Top)
            || placement.Left < int.MinValue || placement.Left > int.MaxValue
            || placement.Top < int.MinValue || placement.Top > int.MaxValue
            || !double.IsFinite(placement.WidthDip) || placement.WidthDip <= 0
            || !double.IsFinite(placement.HeightDip) || placement.HeightDip < 1))
            throw new JsonException("侧边栏位置或尺寸无效");
        if (settings.FollowOffsetDip is { } offset && (!double.IsFinite(offset.X) || !double.IsFinite(offset.Y)))
            throw new JsonException("侧边栏跟随偏移无效");
        return settings;
    }

    public async Task SaveSidePanelPositionAsync(SidePanelPositionSettings settings)
    {
        await _sidePanelPositionLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var path = Path.Combine(_settingsDir, "side-panel-position.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        finally { _sidePanelPositionLock.Release(); }
    }

    public Task SaveTrackingEntriesAsync(List<TrackingEntry> entries)
        => SaveTrackingStateAsync(new TrackingState { Entries = entries });

    public async Task SaveTrackingStateAsync(TrackingState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        Directory.CreateDirectory(_settingsDir);
        var path = Path.Combine(_settingsDir, "tracking.json");
        await _trackingLock.WaitAsync();
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            try
            {
                var previous = await File.ReadAllTextAsync(path);
                if (previous.TrimStart().StartsWith("[", StringComparison.Ordinal))
                    File.Copy(path, path + $".pre-sources-{Guid.NewGuid():N}.bak");
                File.Copy(path, path + ".bak", true);
            }
            catch (FileNotFoundException) { }
            await File.WriteAllTextAsync(temporary, json);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); _trackingLock.Release(); }
    }

    public async Task<List<TrackingEntry>> LoadTrackingEntriesAsync(LineupEquipmentService equipmentRules)
        => (await LoadTrackingStateAsync(equipmentRules)).Entries;

    public async Task<TrackingState> LoadTrackingStateAsync(LineupEquipmentService equipmentRules)
    {
        var path = Path.Combine(_settingsDir, "tracking.json");
        string json;
        try { json = await File.ReadAllTextAsync(path); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(); }
        using var document = JsonDocument.Parse(json);
        var state = document.RootElement.ValueKind == JsonValueKind.Array
            ? new TrackingState { Entries = JsonSerializer.Deserialize<List<TrackingEntry>>(json, JsonOptions)! }
            : JsonSerializer.Deserialize<TrackingState>(json, JsonOptions) ?? throw new JsonException("追踪配置不能为空");
        if (state.Entries is null || state.Lineups is null || state.Lineups.Any(l => l is null || string.IsNullOrEmpty(l.Id))
            || state.Lineups.Select(l => l.Id).Distinct().Count() != state.Lineups.Count
            || state.Entries.Any(e => e is null || e.Origins is null || e.Equipments is null
                || e.Origins.Any(o => o is null || o.LineupId is null || o.Equipments is null)))
            throw new JsonException("追踪来源配置无效");
        var entries = state.Entries;
        var changed = false;
        foreach (var entry in entries.Where(e => e.Type == TrackingType.Operator))
        {
            var slot = new LineupSlot { OperatorName = entry.Name, Equipments = entry.Equipments,
                IsElite = entry.IsElite, TransformedCovenant = entry.TransformedCovenant };
            if (!equipmentRules.UpgradeSlot(slot)) continue;
            entry.TransformedCovenant = slot.TransformedCovenant;
            changed = true;
        }
        if (changed)
        {
            foreach (var name in entries.Where(e => e.Type == TrackingType.Operator && !e.IsAcquired).SelectMany(e => e.Equipments).Distinct().ToList())
                if (!entries.Any(e => e.Type == TrackingType.Equipment && e.Name == name))
                    entries.Add(new TrackingEntry { Name = name, Type = TrackingType.Equipment, IsManual = false });
            File.Copy(path, path + $".pre-equipment-{Guid.NewGuid():N}.bak");
            await SaveTrackingStateAsync(state);
        }
        return state;
    }

    public async Task<string?> LoadSkippedVersionAsync()
    {
        var path = Path.Combine(_settingsDir, "skipped_version.txt");
        if (!File.Exists(path))
            return null;

        var tag = await File.ReadAllTextAsync(path);
        return tag.Trim();
    }

    public async Task SaveSkippedVersionAsync(string tag)
    {
        Directory.CreateDirectory(_settingsDir);
        var path = Path.Combine(_settingsDir, "skipped_version.txt");
        await File.WriteAllTextAsync(path, tag.Trim());
    }

}
