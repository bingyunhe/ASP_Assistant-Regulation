using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ASPAssistant.Core;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    private AssistantHotkey? _assistantHotkey;
    private int _assistantHotkeyId = 0x4154;
    private bool _savingHotkey;
    public bool IsAssistantHidden { get; internal set; }
    public event Action? ToggleAssistantVisibilityRequested;

    public async Task InitAssistantHotkeyAsync()
    {
        try
        {
            var hotkey = await _settingsManager.LoadAssistantHotkeyAsync();
            if (hotkey is null) return;
            if (!TryRegisterAssistantHotkey(hotkey, _assistantHotkeyId, out var error))
            {
                AssistantHotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                AssistantHotkeyStatus.Text = $"保存的快捷键 {HotkeyText(hotkey)} 未生效：{error}";
                return;
            }
            _assistantHotkey = hotkey;
            AssistantHotkeyBox.Text = HotkeyText(hotkey);
            AssistantHotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            AssistantHotkeyStatus.Text = "快捷键已启用";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AssistantHotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            AssistantHotkeyStatus.Text = $"无法读取快捷键：{ex.Message}，请重新设置";
            AppLogger.Warn("Hotkey", AssistantHotkeyStatus.Text);
        }
    }

    private bool TryRegisterAssistantHotkey(AssistantHotkey hotkey, int id, out string? error)
    {
        error = hotkey.ConflictReason();
        if (error is not null) return false;
        if (User32.RegisterHotKey(new WindowInteropHelper(this).Handle, id, hotkey.Modifiers | 0x4000, hotkey.VirtualKey))
            return true;
        var code = Marshal.GetLastWin32Error();
        error = code == 1409 ? "该组合键已被系统或其他程序占用，请换一组" : $"Windows 无法注册快捷键（错误 {code}），请换一组";
        return false;
    }

    private async void OnAssistantHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (_savingHotkey || e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        var hotkey = new AssistantHotkey(
            (modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0) | (modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0) | (modifiers.HasFlag(ModifierKeys.Windows) ? 8u : 0),
            (uint)KeyInterop.VirtualKeyFromKey(key));
        await SaveAssistantHotkeyAsync(hotkey);
    }

    private async void OnClearAssistantHotkey(object sender, RoutedEventArgs e)
        => await SaveAssistantHotkeyAsync(null);

    internal async Task SaveAssistantHotkeyAsync(AssistantHotkey? hotkey)
    {
        if (_savingHotkey || (hotkey is not null && hotkey == _assistantHotkey)) return;
        var nextId = _assistantHotkeyId == 0x4154 ? 0x4155 : 0x4154;
        if (hotkey is not null && !TryRegisterAssistantHotkey(hotkey, nextId, out var error))
        {
            AssistantHotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            AssistantHotkeyStatus.Text = error + "；原设置保留";
            return;
        }
        _savingHotkey = true;
        AssistantHotkeyBox.IsEnabled = ClearHotkeyButton.IsEnabled = false;
        try
        {
            await _settingsManager.SaveAssistantHotkeyAsync(hotkey);
            if (_assistantHotkey is not null) User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, _assistantHotkeyId);
            _assistantHotkeyId = nextId;
            _assistantHotkey = hotkey;
            AssistantHotkeyBox.Text = hotkey is null ? "未设置（点击录入）" : HotkeyText(hotkey);
            AssistantHotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            AssistantHotkeyStatus.Text = hotkey is null ? "已清除快捷键" : "已保存，按下可隐藏 / 显示助手";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (hotkey is not null) User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, nextId);
            AssistantHotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            AssistantHotkeyStatus.Text = $"保存失败：{ex.Message}；原设置保留";
        }
        finally
        {
            _savingHotkey = false;
            AssistantHotkeyBox.IsEnabled = ClearHotkeyButton.IsEnabled = true;
        }
    }

    private static string HotkeyText(AssistantHotkey hotkey)
        => ((hotkey.Modifiers & 2) != 0 ? "Ctrl + " : "") + ((hotkey.Modifiers & 1) != 0 ? "Alt + " : "")
            + ((hotkey.Modifiers & 4) != 0 ? "Shift + " : "") + (hotkey.VirtualKey switch
            {
                0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/",
                0xC0 => "`", 0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
                _ => KeyInterop.KeyFromVirtualKey((int)hotkey.VirtualKey).ToString()
            });

    public void ReleaseAssistantHotkey()
    {
        if (_assistantHotkey is not null)
            User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, _assistantHotkeyId);
    }
}
