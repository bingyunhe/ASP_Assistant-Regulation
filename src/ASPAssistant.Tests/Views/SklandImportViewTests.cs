using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class SklandImportViewTests
{
    public static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var directory = Path.Combine(Path.GetTempPath(), "skland-import-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Operator[] operators = [new() { Name = "甲", Tier = 2 }, new() { Name = "乙", Tier = 3 },
            .. Enumerable.Range(1, 20).Select(i => new Operator { Name = "候选" + i, Tier = (i - 1) % 6 + 1 })];
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var lineups = new LineupViewModel(new LineupStore(Path.Combine(directory, "lineups"), calculator.EquipmentRules), calculator);
        var tracking = new TrackingViewModel();
        var operatorVm = new OperatorBrowseViewModel();
        operatorVm.LoadOperators(operators.ToList());
        var panel = new SidePanelWindow(operatorVm, new EquipmentBrowseViewModel(), tracking, new GameStateViewModel(),
            new BanViewModel(), new EnemyViewModel(), lineups,
            new LineupShareService(calculator.EquipmentRules, Path.Combine(directory, "share")), operators, [], [],
            new SettingsManager(directory), GameShopTheme.Terminal, new SidePanelPositionSettings { FreeMovement = true })
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
        try
        {
            panel.Show();
            var count = 0;
            foreach (var theme in Enum.GetValues<GameShopTheme>())
            foreach (var night in new[] { false, true })
            {
                ThemeManager.Apply(theme, night);
                var draft = new Lineup
                {
                    Name = "官方攻略草稿", SourceUrl = Services.SklandGuideImporterTests.Url,
                    Slots = [new() { OperatorName = "甲", IsElite = true }, new() { OperatorName = "乙" }]
                };
                const string notice = "已读取大成阵容 2 人、策略 0 项。推荐装备 20 件未绑定到干员，回合讲解 2 段未导入；请查看来源攻略。技能与模组沿用本地模式数据。检查并保存后才加入我的阵容，不自动追踪。";
                var cancel = panel.OpenEditorAsync(draft, notice);
                var editor = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
                editor.Width = 760;
                editor.Height = 680;
                editor.UpdateLayout();
                var url = (TextBox)editor.FindName("SourceUrlBox");
                ((Border)editor.FindName("SourcePanel")).Visibility.Should().Be(Visibility.Visible);
                url.Text.Should().Be(draft.SourceUrl);
                url.IsReadOnly.Should().BeTrue();
                ((TextBlock)editor.FindName("ImportNoticeText")).Text.Should().Be(notice);
                url.ActualWidth.Should().BeGreaterThan(100);
                var bounds = url.TransformToAncestor((Visual)editor.Content).TransformBounds(new Rect(url.RenderSize));
                bounds.Right.Should().BeLessThanOrEqualTo(editor.ActualWidth);
                lineups.Lineups.Should().HaveCount(count, "preview must not write the draft");
                Save(editor, $"{theme}-{night}");
                editor.Close();
                Complete(cancel);
                lineups.Lineups.Should().HaveCount(count, "cancel must not save the imported lineup");

                var save = panel.OpenEditorAsync(draft, notice);
                editor = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
                ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Complete(save);
                count++;
                lineups.Lineups.Should().HaveCount(count);
                var saved = lineups.Lineups.Single(x => x.Id == draft.Id);
                saved.SourceUrl.Should().Be(draft.SourceUrl);
                saved.Slots[0].IsElite.Should().BeTrue();
                tracking.TrackedOperators.Should().BeEmpty();
                tracking.TrackedEquipment.Should().BeEmpty();
            }
            Directory.GetFiles(Path.Combine(directory, "lineups"), "*.json")
                .Count(path => !Path.GetFileName(path).StartsWith('.')).Should().BeGreaterThanOrEqualTo(6);
            var candidates = new Lineup
            {
                Name = "20 人候选", SourceUrl = Services.SklandGuideImporterTests.Url,
                Slots = operators.Skip(2).Select(op => new LineupSlot { OperatorName = op.Name }).ToList()
            };
            var select = panel.OpenEditorAsync(candidates, "官方列出20名候选，已全部保留；请筛选至9人以内再保存。");
            var chooser = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
            var host = (ItemsControl)chooser.FindName("SlotsHost");
            host.Items.Count.Should().Be(20);
            ((Button)chooser.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            select.IsCompleted.Should().BeFalse();
            lineups.Lineups.Should().HaveCount(6, "too many candidates must not be saved");
            ((TextBlock)chooser.FindName("EditorHint")).Text.Should().Contain("9");
            for (var i = 0; i < 11; i++)
            {
                var card = (FrameworkElement)host.Items[0];
                Descendants<Button>(card).Single(button => Equals(button.ToolTip, "清空该位置"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            host.Items.Count.Should().Be(9);
            ((Button)chooser.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Complete(select);
            lineups.Lineups.Should().HaveCount(7);
            lineups.Lineups.Single(x => x.Id == candidates.Id).Slots.Should().HaveCount(9);
            candidates.Slots.Should().HaveCount(20);
            tracking.TrackedOperators.Should().BeEmpty();

            var grouped = new Lineup
            {
                Name = "20 人分阶段攻略", SourceUrl = Services.SklandGuideImporterTests.Url,
                EarlySlots = operators.Skip(2).Where(op => op.Tier <= 2).Select(op => new LineupSlot { OperatorName = op.Name }).ToList(),
                MidSlots = operators.Skip(2).Where(op => op.Tier is 3 or 4).Select(op => new LineupSlot { OperatorName = op.Name }).ToList(),
                Slots = operators.Skip(2).Where(op => op.Tier >= 5).Select(op => new LineupSlot { OperatorName = op.Name, IsElite = true }).ToList()
            };
            const string phaseNotice = "官方列出 20 名候选，已按本数分组并全部保留：1–2本前期 8 人、3–4本中期 6 人、5–6本后期 6 人。检查并保存后才加入我的阵容，不自动追踪。";
            foreach (var theme in Enum.GetValues<GameShopTheme>())
            foreach (var night in new[] { false, true })
            {
                ThemeManager.Apply(theme, night);
                var preview = panel.OpenEditorAsync(grouped, phaseNotice);
                var phaseEditor = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
                phaseEditor.Width = 760;
                phaseEditor.Height = 680;
                var tabs = (Panel)phaseEditor.FindName("PhaseTabs");
                foreach (var (phase, people) in new[] { (LineupPhase.前期, 8), (LineupPhase.中期, 6), (LineupPhase.后期, 6) })
                {
                    var tab = tabs.Children.OfType<Button>().Single(button => Equals(button.DataContext, phase));
                    string.Concat(((TextBlock)tab.Content).Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text))
                        .Should().Be($"{phase} {people}");
                    tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((TextBlock)phaseEditor.FindName("EditorHint")).Text.Should().Contain(phase.ToString());
                    Save(phaseEditor, $"phases-{theme}-{night}-{phase}");
                }
                phaseEditor.Close();
                Complete(preview);
                lineups.Lineups.Should().HaveCount(7);
            }
            var saveGrouped = panel.OpenEditorAsync(grouped, phaseNotice);
            var groupedEditor = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
            ((Button)groupedEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Complete(saveGrouped);
            var savedGrouped = lineups.Lineups.Single(lineup => lineup.Id == grouped.Id);
            savedGrouped.EarlySlots.Should().HaveCount(8);
            savedGrouped.MidSlots.Should().HaveCount(6);
            savedGrouped.Slots.Should().HaveCount(6).And.OnlyContain(slot => slot.IsElite);
            tracking.TrackedOperators.Should().BeEmpty();
            tracking.TrackLineup(savedGrouped);
            tracking.TrackedOperators.Should().HaveCount(20);
            tracking.TrackedOperators.Count(entry => entry.Role == TrackingRole.Final).Should().Be(6);
        }
        finally
        {
            foreach (Window child in panel.OwnedWindows.Cast<Window>().ToArray()) child.Close();
            panel.Close();
            Directory.Delete(directory, true);
        }
    }

    private static void Complete(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        }
        task.IsCompleted.Should().BeTrue("the editor should finish saving or cancelling");
        task.GetAwaiter().GetResult();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Save(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "skland-import-previews");
        Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }
}
