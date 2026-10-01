using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 「訪問履歴に含めるインスタンスタイプに滞在中はカウントダウンを停止する」（既定はオン）と、
/// 設定の画面の並びの調整（2026-09-29のユーザー指定→実装メモ5.90）。
/// </summary>
public class StayCountdownTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    [Fact]
    public void 既定では滞在中はカウントダウンを止める()
    {
        Assert.True(new AppSettings().StopCountdownInTarget);
        Assert.True(new EngineOptions { LogDirectory = "x" }.StopCountdownInTarget);

        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.LeftRoom(SessionStart.AddMinutes(3))
            + LogText.Visit(SessionStart.AddMinutes(4), Loc.GroupPublic("222"), "B"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(200), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();

        Assert.Null(harness.Snapshot().RetentionDeadlineUtc);
        Assert.True(harness.Snapshot().CountdownStopped);
        Assert.Equal(2, harness.Snapshot().History.Count);
    }

    [Fact]
    public void 止めないときは入った時刻から数え_期限でいまの滞在の行だけを残して数え直す()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var entered = SessionStart.AddMinutes(4);
        dir.WriteSession(SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.LeftRoom(SessionStart.AddMinutes(3))
            + LogText.Visit(entered, Loc.GroupPublic("222"), "B"));

        using var harness = new EngineHarness(dir.Path, entered.AddMinutes(10), Process(SessionStart), configure: o => o with { StopCountdownInTarget = false });
        harness.Engine.Initialize();
        harness.Engine.Update();

        // 入った時刻から60分。
        Assert.False(harness.Snapshot().CountdownStopped);
        Assert.Equal(harness.Utc(entered.AddMinutes(60)), harness.Snapshot().RetentionDeadlineUtc);

        harness.SetNow(entered.AddMinutes(59));
        harness.Engine.Update();
        Assert.Equal(2, harness.Snapshot().History.Count);

        // 期限が来た。いまの滞在の行（222）だけを残し、そこから数え直す。
        harness.SetNow(entered.AddMinutes(61));
        harness.Engine.Update();

        var history = harness.Snapshot().History;
        Assert.Equal("222", Assert.Single(history).InstanceId);
        Assert.True(harness.Snapshot().RetentionDeadlineUtc > harness.Utc(entered.AddMinutes(61)));
        Assert.True(harness.Snapshot().RetentionDeadlineUtc <= harness.Utc(entered.AddMinutes(120)));

        // 何周期たっても、いまの滞在の行は消えない。
        harness.SetNow(entered.AddMinutes(400));
        harness.Engine.Update();
        Assert.Single(harness.Snapshot().History);
        Assert.True(harness.Snapshot().RetentionDeadlineUtc > harness.Utc(entered.AddMinutes(400)));
    }

    [Fact]
    public void 滞在中に数え直したあと退出しても_滞在していた行は消えない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var entered = SessionStart.AddMinutes(4);
        var left = entered.AddMinutes(200);
        var lines = LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.LeftRoom(SessionStart.AddMinutes(3))
            + LogText.Visit(entered, Loc.GroupPublic("222"), "B");
        var path = dir.WriteSession(SessionStart, lines);

        using var harness = new EngineHarness(dir.Path, entered.AddMinutes(10), Process(SessionStart), configure: o => o with { StopCountdownInTarget = false });
        harness.Engine.Initialize();
        harness.Engine.Update();

        // 滞在の途中で何度か数え直す。
        foreach (var minutes in new[] { 61, 125, 190 })
        {
            harness.SetNow(entered.AddMinutes(minutes));
            harness.Engine.Update();
        }

        // 退出する。滞在していた行（222）は残り、退出から60分を数える。
        File.AppendAllText(path, LogText.LeftRoom(left));
        harness.SetNow(left.AddMinutes(1));
        harness.Engine.Update();

        Assert.Equal("222", Assert.Single(harness.Snapshot().History).InstanceId);
        Assert.Equal(harness.Utc(left.AddMinutes(60)), harness.Snapshot().RetentionDeadlineUtc);

        harness.SetNow(left.AddMinutes(61));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);
    }

    [Fact]
    public void 実行中に止めない側へ切り替えても_過去の長い滞在でまとめて消えず切り替えた時点から数える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var entered = SessionStart.AddMinutes(4);
        dir.WriteSession(SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.LeftRoom(SessionStart.AddMinutes(3))
            + LogText.Visit(entered, Loc.GroupPublic("222"), "B"));

        var switchedAt = entered.AddMinutes(300);
        using var harness = new EngineHarness(dir.Path, switchedAt, Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();

        harness.Engine.SetStopCountdownInTarget(false);

        Assert.Equal(2, harness.Snapshot().History.Count);
        Assert.Equal(harness.Utc(switchedAt.AddMinutes(60)), harness.Snapshot().RetentionDeadlineUtc);

        // 止める側へ戻すと、期限はなくなる。
        harness.Engine.SetStopCountdownInTarget(true);
        Assert.Null(harness.Snapshot().RetentionDeadlineUtc);
    }

    [Fact]
    public void 止めないときは滞在中でも延長が効く()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var entered = SessionStart.AddMinutes(4);
        dir.WriteSession(SessionStart, LogText.Visit(entered, Loc.GroupPublic("222"), "B"));

        using var harness = new EngineHarness(dir.Path, entered.AddMinutes(30), Process(SessionStart), configure: o => o with { StopCountdownInTarget = false });
        harness.Engine.Initialize();
        harness.Engine.Update();

        harness.Engine.ResetRetention();
        Assert.Equal(harness.Utc(entered.AddMinutes(90)), harness.Snapshot().RetentionDeadlineUtc);
    }

    // ---------------------------------------------------------------- 設定の画面

    private static SettingsView Layout(DesktopSettings? settings = null, List<DesktopCommand>? commands = null)
    {
        var view = new SettingsView(new PanelStyle(), settings ?? DesktopSettings.From(new AppSettings()), c => commands?.Add(c));
        view.Layout(new PointF(0f, 0f), 1f, columns: 1);
        return view;
    }

    private static void Click(SettingsView view, SettingsView.HitKind kind)
    {
        var rect = view.TargetRect(kind)!.Value;
        view.PointerDown(new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f)));
        view.PointerUp();
    }

    [Fact]
    public void 滞在中のカウントダウン停止はリセットまでの時間のすぐ下にある()
    {
        var commands = new List<DesktopCommand>();
        using var view = Layout(commands: commands);

        var retention = view.TargetRect(SettingsView.HitKind.StepperPlus, 0)!.Value;
        var stay = view.TargetRect(SettingsView.HitKind.TargetPause)!.Value;
        var afk = view.TargetRect(SettingsView.HitKind.AfkPause)!.Value;

        Assert.True(retention.Bottom <= stay.Y);
        Assert.True(stay.Bottom <= afk.Y);

        Click(view, SettingsView.HitKind.TargetPause);
        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.False(change.Settings.StopCountdownInTarget);
        Assert.Equal(SettingsField.TargetPause, change.Fields);

        var settings = new AppSettings();
        change.Settings.ApplyTo(settings, change.Fields);
        Assert.False(settings.StopCountdownInTarget);
    }

    [Fact]
    public void コントローラーの振動は手首パネルの最下部にあり_パラメータを戻すと一緒に戻る()
    {
        var commands = new List<DesktopCommand>();
        using var view = Layout(DesktopSettings.From(new AppSettings()) with { VibrationEnabled = true }, commands);

        var vibration = view.TargetRect(SettingsView.HitKind.Vibration)!.Value;
        Assert.True(vibration.Y > view.TargetRect(SettingsView.HitKind.ResetWristParameters)!.Value.Bottom);
        Assert.True(vibration.Y < view.TargetRect(SettingsView.HitKind.ResetWarning)!.Value.Y);

        // 「VR オーバーレイ」のまとまりには機能全体の切り替えだけが残る。
        Assert.True(view.TargetRect(SettingsView.HitKind.VrOverlay)!.Value.Y < view.TargetRect(SettingsView.HitKind.WristSide)!.Value.Y);

        Click(view, SettingsView.HitKind.ResetWristParameters);
        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.False(change.Settings.VibrationEnabled);
        Assert.True(change.Fields.HasFlag(SettingsField.Vibration));
    }

    [Fact]
    public void 予告通知の説明は最上部にある()
    {
        using var view = Layout();

        // 見出しの帯と「リセット予告アイコンを表示する」の間に、説明の1行ぶんの空きがある。
        var toggle = view.TargetRect(SettingsView.HitKind.ResetWarning)!.Value;
        var above = view.TargetRect(SettingsView.HitKind.ResetWristParameters)!.Value;
        Assert.True(toggle.Y - above.Bottom > 60f);
    }
}
