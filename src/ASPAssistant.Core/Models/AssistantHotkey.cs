namespace ASPAssistant.Core.Models;

public sealed record AssistantHotkey(uint Modifiers, uint VirtualKey)
{
    public string? ConflictReason()
    {
        if (Modifiers == 0 || (Modifiers & ~15u) != 0 || VirtualKey is < 1 or > 254
            or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5)
            return "请使用 Ctrl、Alt 或 Shift 加一个按键";
        if ((Modifiers & 8) != 0) return "Win 组合键由 Windows 保留，请换一组快捷键";
        if (VirtualKey == 0x7B) return "F12 为系统调试保留键，请换一组快捷键";
        if (Modifiers == 6 && VirtualKey == 0xBF) return "与助手截图快捷键 Ctrl + Shift + / 冲突";
        if (VirtualKey == 0x2C || (Modifiers == 1 && VirtualKey is 0x09 or 0x1B or 0x73)
            || (Modifiers == 2 && VirtualKey == 0x1B)
            || (Modifiers == 6 && VirtualKey == 0x1B)
            || (Modifiers == 3 && VirtualKey == 0x2E)
            || (Modifiers == 5 && VirtualKey == 0x09))
            return "与系统快捷键冲突，请换一组快捷键";
        return null;
    }
}
