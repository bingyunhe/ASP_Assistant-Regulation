using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class AssistantHotkeyTests
{
    [Theory]
    [InlineData(0, 65)]
    [InlineData(8, 65)]
    [InlineData(2, 123)]
    [InlineData(6, 191)]
    [InlineData(1, 9)]
    [InlineData(1, 115)]
    [InlineData(3, 46)]
    [InlineData(6, 27)]
    [InlineData(1, 44)]
    [InlineData(2, 17)]
    [InlineData(32, 65)]
    [InlineData(7, 255)]
    public void ReservedOrInvalidShortcutExplainsConflict(uint modifiers, uint key)
        => new AssistantHotkey(modifiers, key).ConflictReason().Should().NotBeNullOrWhiteSpace();

    [Fact]
    public async Task SelectionPersistsClearsAndKeepsPreviousBackup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "assistant-hotkey-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsManager(folder);
        try
        {
            (await settings.LoadAssistantHotkeyAsync()).Should().BeNull();
            var shortcut = new AssistantHotkey(7, 72);
            shortcut.ConflictReason().Should().BeNull();
            await settings.SaveAssistantHotkeyAsync(shortcut);
            (await new SettingsManager(folder).LoadAssistantHotkeyAsync()).Should().Be(shortcut);
            await settings.SaveAssistantHotkeyAsync(null);
            (await settings.LoadAssistantHotkeyAsync()).Should().BeNull();
            JsonSerializer.Deserialize<AssistantHotkey>(await File.ReadAllTextAsync(Path.Combine(folder, "assistant-hotkey.json.bak")))
                .Should().Be(shortcut);
            await File.WriteAllTextAsync(Path.Combine(folder, "assistant-hotkey.json"), "{\"Modifiers\":8,\"VirtualKey\":65}");
            var load = () => settings.LoadAssistantHotkeyAsync();
            await load.Should().ThrowAsync<JsonException>();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
