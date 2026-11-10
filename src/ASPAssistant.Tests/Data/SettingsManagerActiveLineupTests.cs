// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Data;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

public class SettingsManagerActiveLineupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "aspa-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ActiveLineupId_RoundTripsAndClears()
    {
        var settings = new SettingsManager(_dir);

        (await settings.LoadActiveLineupIdAsync()).Should().BeNull();

        await settings.SaveActiveLineupIdAsync("abc123");
        (await settings.LoadActiveLineupIdAsync()).Should().Be("abc123");

        await settings.SaveActiveLineupIdAsync(null);
        (await settings.LoadActiveLineupIdAsync()).Should().BeNull();
    }

    [Fact]
    public async Task BlankActiveLineupId_IsTreatedAsUnset()
    {
        var settings = new SettingsManager(_dir);
        await settings.SaveActiveLineupIdAsync("   ");
        (await settings.LoadActiveLineupIdAsync()).Should().BeNull();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 测试清理失败不影响断言结果。
        }
    }
}
