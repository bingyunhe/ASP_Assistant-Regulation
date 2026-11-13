using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class UpdateAndTrayTests
{
    public static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var folder = Path.Combine(Path.GetTempPath(), "update-tray-ui-" + Guid.NewGuid().ToString("N"));
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var settings = new SettingsManager(folder);
        var panel = new SidePanelWindow(new OperatorBrowseViewModel(), new EquipmentBrowseViewModel(), new TrackingViewModel(),
            new GameStateViewModel(), new BanViewModel(), new EnemyViewModel(),
            new LineupViewModel(new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules), calculator),
            new LineupShareService(calculator.EquipmentRules, Path.Combine(folder, "share")), [], [], [], settings,
            GameShopTheme.Terminal, new SidePanelPositionSettings { FreeMovement = true })
        {
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0, Left = -20000, Top = -20000, Width = 280, Height = 900
        };
        var overlay = new Window { ShowActivated = false, ShowInTaskbar = false, Opacity = 0, Left = -20000, Top = -20000 };
        var oldFeed = Environment.GetEnvironmentVariable(UpdateService.FeedOverrideVariable);
        try
        {
            panel.Show();
            overlay.Show();
            Pump();
            var hwnd = new WindowInteropHelper(panel).Handle;
            using (var tray = new AssistantTray(panel, overlay))
            {
                tray.VisibilityChanged += () => { if (!panel.IsAssistantHidden) overlay.Show(); };
                var iconField = typeof(AssistantTray).GetField("_icon", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var originalIcon = iconField.GetValue(tray)!;
                var noIcon = iconField.GetValue(tray)!;
                noIcon.GetType().GetField("Icon")!.SetValue(noIcon, IntPtr.Zero);
                iconField.SetValue(tray, noIcon);
                tray.Toggle();
                panel.IsAssistantHidden.Should().BeFalse("without a tray recovery icon the panel must stay visible");
                IsWindowVisible(hwnd).Should().BeTrue();
                iconField.SetValue(tray, originalIcon);
                Await(panel.InitAssistantHotkeyAsync());
                ((TextBox)panel.FindName("AssistantHotkeyBox")).Text.Should().Contain("未设置");
                var keys = new List<AssistantHotkey>();
                using var blocker = new HwndSource(new HwndSourceParameters("shortcut conflict test"));
                for (uint key = 0x70; key < 0x7B && keys.Count < 3; key++)
                {
                    if (!User32.RegisterHotKey(blocker.Handle, (int)key, 7 | 0x4000, key)) continue;
                    keys.Add(new AssistantHotkey(7, key));
                }
                keys.Should().HaveCount(3, "need three unused test shortcuts");
                Await(settings.SaveAssistantHotkeyAsync(keys[1]));
                Await(panel.InitAssistantHotkeyAsync());
                ((TextBlock)panel.FindName("AssistantHotkeyStatus")).Text.Should().Contain("未生效").And.Contain("占用");
                Await(panel.SaveAssistantHotkeyAsync(null));
                Await(settings.LoadAssistantHotkeyAsync()).Should().BeNull("an occupied saved shortcut must still be clearable");
                User32.UnregisterHotKey(blocker.Handle, (int)keys[0].VirtualKey);
                User32.UnregisterHotKey(blocker.Handle, (int)keys[2].VirtualKey);
                try
                {
                    Await(panel.SaveAssistantHotkeyAsync(keys[0]));
                    Await(settings.LoadAssistantHotkeyAsync()).Should().Be(keys[0]);
                    Await(panel.SaveAssistantHotkeyAsync(keys[1]));
                    ((TextBlock)panel.FindName("AssistantHotkeyStatus")).Text.Should().Contain("占用").And.Contain("原设置保留");
                    Await(settings.LoadAssistantHotkeyAsync()).Should().Be(keys[0]);
                    Await(panel.SaveAssistantHotkeyAsync(new AssistantHotkey(6, 0xBF)));
                    ((TextBlock)panel.FindName("AssistantHotkeyStatus")).Text.Should().Contain("截图");
                    Await(settings.LoadAssistantHotkeyAsync()).Should().Be(keys[0]);
                    var activeId = (int)typeof(SidePanelWindow).GetField("_assistantHotkeyId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
                    SendMessage(hwnd, User32.WM_HOTKEY, activeId, IntPtr.Zero);
                    panel.IsAssistantHidden.Should().BeTrue();
                    IsWindowVisible(hwnd).Should().BeFalse();
                    overlay.IsVisible.Should().BeFalse();
                    SendMessage(hwnd, User32.WM_HOTKEY, activeId, IntPtr.Zero);
                    panel.IsAssistantHidden.Should().BeFalse();
                    IsWindowVisible(hwnd).Should().BeTrue();
                    overlay.IsVisible.Should().BeTrue();
                    using (var lockedSettings = new FileStream(Path.Combine(folder, "assistant-hotkey.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        Await(panel.SaveAssistantHotkeyAsync(keys[2]));
                        ((TextBlock)panel.FindName("AssistantHotkeyStatus")).Text.Should().Contain("保存失败").And.Contain("原设置保留");
                    }
                    Await(settings.LoadAssistantHotkeyAsync()).Should().Be(keys[0]);
                    File.Exists(Path.Combine(folder, "assistant-hotkey.json.tmp")).Should().BeFalse();
                    Await(panel.SaveAssistantHotkeyAsync(keys[2]));
                    User32.RegisterHotKey(blocker.Handle, (int)keys[0].VirtualKey, 7, keys[0].VirtualKey).Should().BeTrue("changing releases the old shortcut");
                    User32.UnregisterHotKey(blocker.Handle, (int)keys[0].VirtualKey);
                    panel.ReleaseAssistantHotkey();
                    Await(panel.InitAssistantHotkeyAsync());
                    ((TextBlock)panel.FindName("AssistantHotkeyStatus")).Text.Should().Be("快捷键已启用");
                    Await(panel.SaveAssistantHotkeyAsync(null));
                    Await(settings.LoadAssistantHotkeyAsync()).Should().BeNull();
                    User32.RegisterHotKey(blocker.Handle, (int)keys[2].VirtualKey, 7, keys[2].VirtualKey).Should().BeTrue("clearing releases the shortcut");
                    User32.UnregisterHotKey(blocker.Handle, (int)keys[2].VirtualKey);

                    var editor = new Window { Owner = panel, ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
                        Content = new TextBox { Text = "未保存的阵容" }, Left = -20000, Top = -20000 };
                    var modalReturned = false;
                    panel.Dispatcher.BeginInvoke(() =>
                    {
                        tray.Toggle();
                        IsWindowVisible(new WindowInteropHelper(editor).Handle).Should().BeFalse();
                        Pump();
                        modalReturned.Should().BeFalse("hiding must not end the modal editor");
                        SendMessage(hwnd, 0x8000 + 73, IntPtr.Zero, new IntPtr(0x400));
                        panel.IsAssistantHidden.Should().BeFalse("tray activation restores");
                        ((TextBox)editor.Content).Text.Should().Be("未保存的阵容");
                        IsWindowVisible(new WindowInteropHelper(editor).Handle).Should().BeTrue();
                        editor.Close();
                    });
                    editor.ShowDialog();
                    modalReturned = true;
                    // Screenshot capture may temporarily hide the panel before the shortcut arrives.
                    panel.Hide();
                    tray.Toggle();
                    tray.Restore();
                    panel.IsVisible.Should().BeTrue();
                }
                finally { User32.UnregisterHotKey(blocker.Handle, (int)keys[1].VirtualKey); }
            }

            using var server = new TcpListener(IPAddress.Loopback, 0);
            server.Start();
            var address = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}";
            Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, address + "/latest");
            var feed = Task.Run(() => Respond(server, 200, JsonSerializer.Serialize(new
            {
                tag_name = "v99.9.9", body = "测试版本说明", assets = new[] {
                    new { name = "test-win-x64.zip", browser_download_url = address + "/package.zip" } }
            })));
            Await(panel.CheckForUpdatesAsync(true));
            Await(feed);
            server.Pending().Should().BeFalse("detection must not download the package");
            ((TextBlock)panel.FindName("UpdateStatusText")).Text.Should().Contain("选择下载");
            var bannerAction = Descendants<Button>(panel).Single(b => Descendants<TextBlock>(b).Any(t => t.Name == "UpdateActionText"));
            var requestServed = false;
            ((Button)panel.FindName("SkipVersionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var skipSave = settings.LoadSkippedVersionAsync();
            Await(skipSave);
            Pump();
            ((Border)panel.FindName("UpdateBanner")).Visibility.Should().Be(Visibility.Collapsed);
            server.Pending().Should().BeFalse("skipping the version must not download");
            panel.PresentAvailableUpdate(new UpdateInfo("v99.9.9", "99.9.9", "测试版本说明", address + "/package.zip", "test-win-x64.zip", null));
            panel.Dispatcher.BeginInvoke(() =>
            {
                var dialog = Application.Current.Windows.OfType<UpdateDialog>().Single();
                ((ProgressBar)dialog.FindName("DialogProgress")).Visibility.Should().Be(Visibility.Collapsed);
                server.Pending().Should().BeFalse("viewing release notes must not download");
                dialog.Close();
            });
            bannerAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            server.Pending().Should().BeFalse("closing notes must not download");
            panel.Dispatcher.BeginInvoke(() =>
            {
                var dialog = Application.Current.Windows.OfType<UpdateDialog>().Single();
                var request = Task.Run(() => Respond(server, 503, "unavailable"));
                ((Button)dialog.FindName("DownloadButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Await(request);
                requestServed = true;
                var download = (Task)typeof(SidePanelWindow).GetField("_downloadTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
                Await(download);
                Pump();
                ((TextBlock)dialog.FindName("DownloadHintText")).Text.Should().Contain("503").And.Contain("重试");
                ((Button)dialog.FindName("DownloadButton")).Visibility.Should().Be(Visibility.Visible);
                dialog.Close();
            });
            bannerAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            requestServed.Should().BeTrue("only the explicit download action requests the package");
            foreach (var theme in Enum.GetValues<GameShopTheme>())
            foreach (var night in new[] { false, true })
            {
                ThemeManager.Apply(theme, night);
                ((Button)panel.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForAnimation();
                var settingsPanel = (Border)panel.FindName("SettingsPanel");
                Descendants<ScrollViewer>(settingsPanel).First().ScrollToBottom();
                Pump();
                var hotkeyBox = (TextBox)panel.FindName("AssistantHotkeyBox");
                var bounds = hotkeyBox.TransformToAncestor(panel).TransformBounds(new Rect(hotkeyBox.RenderSize));
                bounds.Right.Should().BeLessThanOrEqualTo(panel.ActualWidth);
                bounds.Bottom.Should().BeLessThanOrEqualTo(panel.ActualHeight);
                var title = (Border)panel.FindName("StatusChip");
                Descendants<TextBlock>(title).Should().Contain(t => t.Text == "终端");
                var titleBounds = title.TransformToAncestor(panel).TransformBounds(new Rect(title.RenderSize));
                var settingsBounds = ((Button)panel.FindName("SettingsButton")).TransformToAncestor(panel)
                    .TransformBounds(new Rect(((Button)panel.FindName("SettingsButton")).RenderSize));
                titleBounds.Right.Should().BeLessThanOrEqualTo(settingsBounds.Left + 1);
                Save((FrameworkElement)panel.Content, $"{theme}-{night}-settings-280");
                ((Button)panel.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForAnimation();
            }
            var exitTray = new AssistantTray(panel, overlay);
            panel.Close();
            exitTray.Dispose();
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, oldFeed);
            panel.ReleaseAssistantHotkey();
            User32.UnregisterHotKey(new WindowInteropHelper(panel).Handle, 0x4153);
            panel.Close();
            overlay.Close();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static void Respond(TcpListener server, int status, string body)
    {
        using var client = server.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        while (!string.IsNullOrEmpty(reader.ReadLine())) { }
        var bytes = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        stream.Write(headers);
        stream.Write(bytes);
    }

    private static void WaitForAnimation()
    {
        var delay = Task.Delay(300);
        Await(delay);
        Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Await(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump();
        task.IsCompleted.Should().BeTrue("asynchronous operation must finish");
        task.GetAwaiter().GetResult();
    }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
    private static void Save(FrameworkElement view, string name)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "update-tray-previews");
        Directory.CreateDirectory(folder);
        var image = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(output);
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
}
