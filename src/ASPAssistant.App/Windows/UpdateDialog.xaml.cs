using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class UpdateDialog : Window
{
    public event Action? DownloadRequested;

    public UpdateDialog(UpdateInfo updateInfo)
    {
        InitializeComponent();

        TitleText.Text = $"发现新版本 {updateInfo.TagName}";

        var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        CurrentVersionText.Text = currentVersion == new Version(0, 0, 0) || currentVersion is null
            ? "0.0.0 (dev)"
            : currentVersion.ToString(3);

        LatestVersionText.Text = updateInfo.Version;
        // 公告保留标题和更新内容；下载使用说明留在发布页。
        var notes = updateInfo.ReleaseNotes;
        var nextSection = Regex.Match(notes, @"(?m)^##[ \t]+(?!更新内容[ \t]*\r?$)\S");
        if (nextSection.Success)
            notes = notes[..nextSection.Index];
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(notes)
            ? "（暂无更新说明）"
            : notes.Trim();
    }

    /// <summary>把下载进度（0～1）与阶段文案同步到对话框。</summary>
    public void ReportProgress(double fraction)
    {
        DownloadButton.Visibility = Visibility.Collapsed;
        DialogProgress.Visibility = Visibility.Visible;
        ProgressPercentText.Visibility = Visibility.Visible;
        var percent = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
        DialogProgress.Value = percent;
        ProgressPercentText.Text = percent + "%";
        DownloadHintText.Text = fraction switch
        {
            < 0.85 => "正在下载安装包…",
            < 0.95 => "正在校验安装包…",
            < 1.0 => "正在解压安装包…",
            _ => "下载完成，点击主界面横幅上的“立即重启”完成更新。"
        };
    }

    public void ReportFailure(string reason)
    {
        DownloadHintText.Text = $"下载失败：{reason}。可点击“重试下载”。";
        DownloadButton.Content = "重试下载";
        DownloadButton.Visibility = Visibility.Visible;
        DialogProgress.Visibility = Visibility.Collapsed;
        ProgressPercentText.Visibility = Visibility.Collapsed;
    }

    private void OnDownload(object sender, RoutedEventArgs e)
    {
        ReportProgress(0);
        DownloadRequested?.Invoke();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
