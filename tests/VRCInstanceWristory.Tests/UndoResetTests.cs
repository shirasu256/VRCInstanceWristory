using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 直前のリセットを戻す操作と、同じ日に足した手首パネルまわりの設定（2026-09-28のユーザー指定→実装メモ5.86）。
///
/// - 「直前のリセットを戻す」（リセットまでの時間の下）。手のリセットも自動リセットも、行・目印・回数ごと戻す
/// - 「トリガーで操作メニューを表示」（手首パネルの位置をデフォルトに戻すの上。既定はオン）
/// - 「手首パネルの位置をデフォルトに戻す」（配置だけを戻す）
/// - 既定で記録する種類に Group+ を足した
/// </summary>
public class UndoResetTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    private static string TwoVisitsAndLeave()
        => LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
           + LogText.LeftRoom(SessionStart.AddMinutes(3))
           + LogText.Visit(SessionStart.AddMinutes(4), Loc.GroupPublic("222"), "B")
           + LogText.LeftRoom(SessionStart.AddMinutes(6));

    [Fact]
    public void 手のリセットを戻すと行と目印と回数が戻り_もう一度は戻せない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, TwoVisitsAndLeave());
        using var tempFile = new TempFile("marks");
        var marksPath = tempFile.Path;

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(7), Process(SessionStart), marks: new MarkFile(marksPath));
        harness.Engine.Initialize();
        harness.Engine.Update();

        var before = harness.Snapshot().History;
        Assert.Equal(["111", "222"], before.Select(v => v.InstanceId));
        harness.Engine.SetMark(before[0].EventId, InstanceMark.Heart);
        Assert.False(harness.Engine.CanUndoClearHistory);

        harness.Engine.ClearHistory();
        Assert.Empty(harness.Snapshot().History);
        Assert.Empty(harness.Snapshot().Marks);
        Assert.True(harness.Engine.CanUndoClearHistory);

        harness.Engine.UndoClearHistory();

        var restored = harness.Snapshot();
        Assert.Equal(["111", "222"], restored.History.Select(v => v.InstanceId));
        Assert.Equal([1, 1], restored.History.Select(v => v.VisitOrdinal));
        Assert.Equal(InstanceMark.Heart, restored.Marks[before[0].LocationKey]);
        Assert.Equal(InstanceMark.Heart, new MarkFile(marksPath).Load()[before[0].LocationKey].Mark);
        Assert.False(harness.Engine.CanUndoClearHistory);

        // 戻したあとも Update で消えない。数え直しは戻した時点から。
        harness.SetNow(SessionStart.AddMinutes(8));
        harness.Engine.Update();
        Assert.Equal(2, harness.Snapshot().History.Count);
        Assert.Equal(harness.Utc(SessionStart.AddMinutes(7 + 60)), harness.Snapshot().RetentionDeadlineUtc);
    }

    [Fact]
    public void 自動リセットも戻せ_戻した行はすぐには消えない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, TwoVisitsAndLeave());

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(7), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();

        // 退出から60分でまとめて消える。
        var expiredAt = SessionStart.AddMinutes(6 + 61);
        harness.SetNow(expiredAt);
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);
        Assert.True(harness.Engine.CanUndoClearHistory);

        harness.Engine.UndoClearHistory();
        Assert.Equal(["111", "222"], harness.Snapshot().History.Select(v => v.InstanceId));

        // 戻した時点から60分は残り、そこで再び消える。
        harness.SetNow(expiredAt.AddMinutes(59));
        harness.Engine.Update();
        Assert.Equal(2, harness.Snapshot().History.Count);

        harness.SetNow(expiredAt.AddMinutes(61));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);
    }

    [Fact]
    public void 消す行のないリセットでは戻せない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, TwoVisitsAndLeave());

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(7), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();

        harness.Engine.ClearHistory();
        harness.SetNow(SessionStart.AddMinutes(8));
        harness.Engine.ClearHistory(); // 消す行はない。直前（1回目）を戻せるまま。

        harness.Engine.UndoClearHistory();
        Assert.Equal(2, harness.Snapshot().History.Count);
    }

    // ------------------------------------------------------------------ 設定の画面

    private static DesktopView View(DesktopTab tab, DesktopSettings settings, List<DesktopCommand> commands)
    {
        var view = new DesktopView(new PanelStyle(), settings, commands.Add);
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SelectTab(tab);
        return view;
    }

    private static PointF Center(RectangleF rect) => new(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));

    [Fact]
    public void 直前のリセットを戻すはリセットまでの時間の下で_戻せるときだけ押せる()
    {
        var commands = new List<DesktopCommand>();
        using var view = View(DesktopTab.Startup, DesktopSettings.From(new AppSettings()), commands);

        var undo = view.TargetRect(SettingsView.HitKind.UndoClear)!.Value;
        var plus = view.TargetRect(SettingsView.HitKind.StepperPlus, 0)!.Value;
        Assert.True(undo.Top >= plus.Bottom);

        // 戻せるリセットがない間は押せず、指すと理由を出す。
        Assert.False(view.IsClickable(Center(undo)));
        view.MouseDown(Center(undo));
        view.MouseUp();
        Assert.Empty(commands);

        using var available = View(DesktopTab.Startup, DesktopSettings.From(new AppSettings { UndoResetAvailable = true }), commands);
        available.MouseDown(Center(undo));
        available.MouseUp();
        Assert.IsType<DesktopCommand.UndoClearHistory>(Assert.Single(commands));
    }

    [Fact]
    public void トリガーで操作メニューを表示は既定でオンで_位置を戻すボタンの上にあり_その項目だけを送る()
    {
        Assert.True(new AppSettings().TriggerMenuEnabled);

        var commands = new List<DesktopCommand>();
        using var view = View(DesktopTab.Panel, DesktopSettings.From(new AppSettings()), commands);

        var trigger = view.TargetRect(SettingsView.HitKind.TriggerMenu)!.Value;
        var reset = view.TargetRect(SettingsView.HitKind.ResetPlacement)!.Value;
        var scroll = view.TargetRect(SettingsView.HitKind.StepperPlus, 5)!.Value;
        Assert.True(trigger.Top >= scroll.Bottom);
        Assert.True(reset.Top >= trigger.Bottom);

        view.MouseDown(new PointF(trigger.X + 4f, trigger.Y + (trigger.Height / 2f)));
        view.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(SettingsField.TriggerMenu, change.Fields);
        Assert.False(change.Settings.TriggerMenuEnabled);
    }

    [Fact]
    public void VRオーバーレイ機能をオフにしている間はトリガーの切り替えも押せない()
    {
        var commands = new List<DesktopCommand>();
        using var view = View(DesktopTab.Panel, DesktopSettings.From(new AppSettings()) with { VrOverlayEnabled = false }, commands);

        var trigger = view.TargetRect(SettingsView.HitKind.TriggerMenu)!.Value;
        Assert.False(view.IsClickable(new PointF(trigger.X + 4f, trigger.Y + (trigger.Height / 2f))));
    }

    [Fact]
    public void トリガーの切り替えは設定ファイルへ書き戻せる()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        new AppSettings { TriggerMenuEnabled = false }.SaveFields(path, SettingsField.TriggerMenu);
        Assert.False(AppSettings.Load(path, new CollectingDiagnostics()).TriggerMenuEnabled);
        Assert.Contains("\"triggerMenuEnabled\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void 位置を戻すボタンは位置を戻す命令だけを送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = View(DesktopTab.Panel, DesktopSettings.From(new AppSettings()), commands);

        var reset = view.TargetRect(SettingsView.HitKind.ResetPlacement)!.Value;
        view.MouseDown(Center(reset));
        view.MouseUp();

        Assert.IsType<DesktopCommand.ResetPlacement>(Assert.Single(commands));
    }

    [Fact]
    public void 既定で記録する種類にGroupPlusを含む()
    {
        Assert.Equal(
            [AccessType.Public, AccessType.GroupPublic, AccessType.GroupPlus, AccessType.GroupOnly],
            TargetAccessTypes.Selectable.Where(TargetAccessTypes.Default.Contains));
        Assert.True(new AppSettings().TargetTypes.SetEquals(TargetAccessTypes.Default));
        Assert.Contains(AccessType.GroupPlus, TargetAccessTypes.Default);
    }
}
