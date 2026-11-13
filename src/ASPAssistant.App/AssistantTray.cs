using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ASPAssistant.App.Windows;
using ASPAssistant.Core;

namespace ASPAssistant.App;

internal sealed class AssistantTray : IDisposable
{
    private const int CallbackMessage = 0x8000 + 73;
    private readonly SidePanelWindow _panel;
    private readonly Window _overlay;
    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private readonly List<(Window Window, IntPtr Handle)> _hiddenWindows = [];
    private NotifyIconData _icon;
    private bool _added;
    public event Action? VisibilityChanged;

    public AssistantTray(SidePanelWindow panel, Window overlay)
    {
        _panel = panel;
        _overlay = overlay;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(panel).Handle);
        _source.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreated == 0) AppLogger.Warn("Tray", "无法注册任务栏恢复通知，托盘恢复可使用快捷键");
        _icon = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _source.Handle, Id = 1,
            Flags = 1 | 2 | 4, CallbackMessage = CallbackMessage,
            Tip = "鸿雪·卫戍助手 · 点击恢复助手", Info = "", InfoTitle = ""
        };
        var icons = new IntPtr[1];
        if (ExtractIconEx(Path.Combine(AppContext.BaseDirectory, "ASPAssistant.App.exe"), 0, null, icons, 1) == 1)
            _icon.Icon = icons[0];
        panel.ToggleAssistantVisibilityRequested += Toggle;
    }

    public void Toggle()
    {
        if (_panel.IsAssistantHidden) { Restore(); return; }
        if (!AddIcon())
        {
            _panel.ShowToast("无法创建托盘图标，助手保持显示，请稍后重试");
            return;
        }
        _added = true;
        // 保持 WPF 模态状态，避免 Hide() 提前结束 ShowDialog() 并丢失编辑内容。
        _hiddenWindows.Clear();
        foreach (Window window in Application.Current.Windows)
        {
            if (window == _overlay || (!window.IsVisible && window != _panel)) continue;
            _hiddenWindows.Add((window, new WindowInteropHelper(window).Handle));
        }
        _panel.IsAssistantHidden = true;
        _overlay.Hide();
        foreach (var window in _hiddenWindows) ShowWindow(window.Handle, 0);
        VisibilityChanged?.Invoke();
    }

    public void Restore()
    {
        if (!_panel.IsAssistantHidden) return;
        _panel.IsAssistantHidden = false;
        foreach (var window in _hiddenWindows)
        {
            if (window.Window == _panel && !window.Window.IsVisible)
            {
                var activate = _panel.ShowActivated;
                _panel.ShowActivated = false;
                _panel.Show();
                _panel.ShowActivated = activate;
            }
            if (window.Window.IsVisible && PresentationSource.FromVisual(window.Window) is HwndSource source
                && source.Handle == window.Handle)
                ShowWindow(window.Handle, 8); // SW_SHOWNA：保留各窗口原先的最小化状态。
        }
        _hiddenWindows.Clear();
        RemoveIcon();
        VisibilityChanged?.Invoke();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage && (lParam.ToInt64() & 0xFFFF) is 0x202 or 0x205 or 0x400 or 0x401 or 0x7B)
        {
            Restore();
            handled = true;
        }
        if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated && _panel.IsAssistantHidden)
        {
            _added = AddIcon();
            if (!_added)
            {
                Restore();
                _panel.ShowToast("托盘图标恢复失败，已显示助手");
            }
        }
        return IntPtr.Zero;
    }

    private bool AddIcon()
    {
        if (_icon.Icon == IntPtr.Zero || !ShellNotifyIcon(0, ref _icon)) return false;
        _icon.TimeoutOrVersion = 4;
        if (ShellNotifyIcon(4, ref _icon)) return true;
        ShellNotifyIcon(2, ref _icon);
        return false;
    }

    private void RemoveIcon()
    {
        if (_added) ShellNotifyIcon(2, ref _icon);
        _added = false;
    }

    public void Dispose()
    {
        _panel.ToggleAssistantVisibilityRequested -= Toggle;
        if (!_source.IsDisposed) _source.RemoveHook(WndProc);
        RemoveIcon();
        if (_icon.Icon != IntPtr.Zero) DestroyIcon(_icon.Icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[] small, uint count);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}
