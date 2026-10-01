using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 履歴の自動リセットの切り替えと、VRオーバーレイ機能の切り替え（2026-09-27のユーザー指定→実装メモ5.71）。
/// </summary>
public class AutoResetTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    [Fact]
    public void 自動リセットを無効にすると期限を過ぎても行が残り_延長もしない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(leftAt));

        using var harness = new EngineHarness(dir.Path, leftAt.AddMinutes(1), Process(SessionStart), configure: o => o with { AutoReset = false });
        harness.Engine.Initialize();
        harness.Engine.Update();

        Assert.False(harness.Engine.AutoReset);
        Assert.Null(harness.Snapshot().RetentionDeadlineUtc);

        harness.Engine.ResetRetention();
        harness.SetNow(leftAt.AddHours(20));
        harness.Engine.Update();

        Assert.Single(harness.Snapshot().History);

        // 手で消す「リセット」は効く。
        harness.Engine.ClearHistory();
        Assert.Empty(harness.Snapshot().History);
    }

    [Fact]
    public void 自動リセットを戻しても_無効の間にたまった行はまとめて消えず_戻した時点から数える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(leftAt));
        using var tempFile = new TempFile("checkpoint");
        var path = tempFile.Path;

        var enabledAt = leftAt.AddHours(5);

        using (var first = new EngineHarness(dir.Path, leftAt.AddMinutes(1), Process(SessionStart), new CheckpointStore(path), o => o with { AutoReset = false }))
        {
            first.Engine.Initialize();
            first.Engine.Update();

            // 退出から5時間後に戻す。退出+60分の期限はとうに過ぎているが、消えない。
            first.SetNow(enabledAt);
            first.Engine.SetAutoReset(true);
            first.Engine.Update();

            Assert.Single(first.Snapshot().History);
            Assert.Equal(first.Utc(enabledAt.AddMinutes(60)), first.Snapshot().RetentionDeadlineUtc);
        }

        // 立ち上げ直しても、戻した時刻より前の退出からは数えない。
        using var second = new EngineHarness(dir.Path, enabledAt.AddMinutes(30), Process(SessionStart), new CheckpointStore(path));
        second.Engine.Initialize();
        second.Engine.Update();
        Assert.Single(second.Snapshot().History);

        // 戻した時点から60分でまとめて消える。
        second.SetNow(enabledAt.AddMinutes(60));
        second.Engine.Update();
        Assert.Empty(second.Snapshot().History);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void 自動リセットが無効なら_回数はVRChatの起動ごとに1から数える(bool autoReset, int second)
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        // 1回目の起動で入って終了し、10分後に起動し直して同じインスタンスへ入る（有効なら60分以内なので引き継ぐ）。
        var first = SessionStart;
        dir.WriteSession(first, LogText.Visit(first.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(first.AddMinutes(5)) + LogText.Quit(first.AddMinutes(6)));
        var next = first.AddMinutes(16);
        dir.WriteSession(next, LogText.Visit(next.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, next.AddMinutes(2), Process(next), configure: o => o with { AutoReset = autoReset });
        harness.Engine.Initialize();
        harness.Engine.Update();

        Assert.Equal(second, harness.Snapshot().History[^1].VisitOrdinal);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void 自動リセットが入ると回数を1から数え直し_直前のリセットを戻すと回数も戻る(bool undo, int expected)
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(SessionStart.AddMinutes(3)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(4), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();
        Assert.Equal(1, harness.Snapshot().History.Single().VisitOrdinal);

        // 退出から60分で自動リセットが入る（2026-10-01のユーザー指定→実装メモ5.110）。
        harness.SetNow(SessionStart.AddMinutes(63));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);

        if (undo)
        {
            harness.Engine.UndoClearHistory();
            Assert.Single(harness.Snapshot().History);
        }

        var back = SessionStart.AddMinutes(70);
        TempLogDirectory.Append(file, LogText.Visit(back, Loc.GroupPublic("111"), "A"));
        harness.SetNow(back.AddMinutes(1));
        harness.Engine.Update();

        Assert.Equal(expected, harness.Snapshot().History[^1].VisitOrdinal);
    }

    [Fact]
    public void 自動リセットを無効にすると見出しは残り時間と延長の代わりに断りを出し_日付まで出す()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);
        var rows = SampleRows.Build();
        renderer.RenderRows(renderer.Measure(rows));

        renderer.Compose(0f, new PanelDecorations { Countdown = "60:00", AutoResetDisabled = true });

        Assert.True(renderer.ResetButtonRectFor("60:00").IsEmpty);
        Assert.True(renderer.CountdownRectFor("60:00").IsEmpty);
        Assert.False(renderer.ClearButtonRectFor("60:00").IsEmpty); // 手で消す「リセット」は残す
        Assert.Matches(@"~\)$", renderer.HeaderTitleText);
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}~", renderer.HeaderTitleText);
        Assert.DoesNotContain("全", renderer.HeaderTitleText); // 件数は出さない（→実装メモ5.109）

        // 断りは見出しの左の表題と重ならない。
        Assert.True(renderer.HeaderTitleRect(PanelRenderer.HeaderTitle(999, "2026-09-27 23:59")).Right < renderer.AutoResetDisabledRect().Left);

        renderer.Compose(0f, new PanelDecorations { Countdown = "60:00" });
        Assert.False(renderer.ResetButtonRectFor("60:00").IsEmpty);
        Assert.DoesNotContain("-", renderer.HeaderTitleText.Split('(')[1].Split(' ')[1]);
    }

    [Fact]
    public void 自動リセットを無効にするとリセットまでの時間は動かせず_値は横棒になる()
    {
        using var view = new SettingsDashboardView(new PanelStyle(), DesktopSettings.From(new AppSettings()) with { AutoResetEnabled = false }, _ => { });

        Assert.NotNull(view.TargetRect(SettingsView.HitKind.AutoReset));
        Assert.Equal("60 分", view.StepperText(0));

        var commands = new List<DesktopCommand>();
        using var desktop = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()) with { AutoResetEnabled = false }, commands.Add);
        desktop.Resize(DesktopView.DefaultClientSize, 1f);

        var plus = desktop.TargetRect(SettingsView.HitKind.StepperPlus, 0)!.Value;
        var center = new PointF(plus.X + (plus.Width / 2f), plus.Y + (plus.Height / 2f));
        desktop.MouseMove(center);
        Assert.False(desktop.IsClickable(center));
        desktop.MouseDown(center);
        desktop.MouseUp();
        Assert.Empty(commands);

        // 切り替えは押せて、変更を送る。
        var toggle = desktop.TargetRect(SettingsView.HitKind.AutoReset)!.Value;
        desktop.MouseDown(new PointF(toggle.X + 4f, toggle.Y + (toggle.Height / 2f)));
        desktop.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.True(change.Settings.AutoResetEnabled);
        Assert.Equal(SettingsField.AutoReset, change.Fields);
    }

    [Fact]
    public void VRオーバーレイ機能をオフにすると手首パネルの設定は押せない()
    {
        var commands = new List<DesktopCommand>();
        using var desktop = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()) with { VrOverlayEnabled = false }, commands.Add);
        desktop.Resize(DesktopView.DefaultClientSize, 1f);
        desktop.SelectTab(DesktopTab.Panel);

        foreach (var kind in new[] { SettingsView.HitKind.WristSide, SettingsView.HitKind.ShowDuringLoading, SettingsView.HitKind.ResetPlacement, SettingsView.HitKind.StepperPlus })
        {
            var rect = desktop.TargetRect(kind, kind == SettingsView.HitKind.StepperPlus ? 1 : kind == SettingsView.HitKind.WristSide ? 1 : 0)!.Value;
            var center = new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));
            Assert.False(desktop.IsClickable(center));
            desktop.MouseDown(center);
            desktop.MouseUp();
        }

        Assert.Empty(commands);

        var toggle = desktop.TargetRect(SettingsView.HitKind.VrOverlay)!.Value;
        desktop.MouseDown(new PointF(toggle.X + 4f, toggle.Y + (toggle.Height / 2f)));
        desktop.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.True(change.Settings.VrOverlayEnabled);
        Assert.Equal(SettingsField.VrOverlay, change.Fields);
    }

    [Fact]
    public void 対象に滞在している間はカウントダウンが止まっていると知らせ_離れると数え始める()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();
        Assert.True(harness.Snapshot().CountdownStopped);

        TempLogDirectory.Append(file, LogText.LeftRoom(SessionStart.AddMinutes(3)));
        harness.SetNow(SessionStart.AddMinutes(4));
        harness.Engine.Update();
        Assert.False(harness.Snapshot().CountdownStopped);

        // 自動リセットを止めている間は「停止中」ではない（見出しは断りになる→5.71）。
        harness.Engine.SetAutoReset(false);
        Assert.False(harness.Snapshot().CountdownStopped);
    }

    [Fact]
    public void カウントダウンが止まっている間は見出しを停止中にし_延長だけ押せない()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);
        renderer.RenderRows(renderer.Measure(SampleRows.Build()));

        renderer.Compose(0f, new PanelDecorations { Countdown = "60:00", CountdownStopped = true });

        Assert.Equal("カウントダウン停止中:", renderer.CurrentCountdownLabel);
        Assert.True(renderer.ResetButtonRectFor("60:00").IsEmpty);
        Assert.False(renderer.ClearButtonRectFor("60:00").IsEmpty); // 「履歴リセット」はいつでも押せる（→実装メモ5.74）
        Assert.False(renderer.CountdownRectFor("60:00").IsEmpty); // 残り時間（60:00）はそのまま出す

        // 長くなった文字も、見出しの左の表題と重ならない。
        Assert.True(renderer.HeaderTitleRect(PanelRenderer.HeaderTitle(999, "23:59")).Right < renderer.CountdownLabelRectFor("60:00").Left);

        renderer.Compose(0f, new PanelDecorations { Countdown = "59:00" });
        Assert.Equal("履歴リセットまで:", renderer.CurrentCountdownLabel);
        Assert.False(renderer.ResetButtonRectFor("59:00").IsEmpty);

        // ウィンドウでも押せない。
        var commands = new List<DesktopCommand>();
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetRows(SampleRows.Build());
        view.SetCountdown("60:00");

        var extend = view.ResetButtonScreenRect();
        var clear = view.ClearButtonScreenRect();
        view.SetCountdownStopped(true);

        var extendCenter = new PointF(extend.X + (extend.Width / 2f), extend.Y + (extend.Height / 2f));
        Assert.False(view.IsClickable(extendCenter));
        view.MouseDown(extendCenter);
        view.MouseUp();
        Assert.Empty(commands);

        // 「履歴リセット」は押せて、確認を出す。
        var clearCenter = new PointF(clear.X + (clear.Width / 2f), clear.Y + (clear.Height / 2f));
        Assert.True(view.IsClickable(clearCenter));
        view.MouseDown(clearCenter);
        Assert.True(view.ClearConfirmOpen);
    }

    [Fact]
    public void 自動リセットとVRオーバーレイ機能の切り替えは設定ファイルへ書き戻せる()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        var settings = new AppSettings { AutoResetEnabled = false, VrOverlayEnabled = false };
        settings.SaveFields(path, SettingsField.AutoReset | SettingsField.VrOverlay);

        var loaded = AppSettings.Load(path, new CollectingDiagnostics());
        Assert.False(loaded.AutoResetEnabled);
        Assert.False(loaded.VrOverlayEnabled);
        Assert.True(new AppSettings().AutoResetEnabled);
        Assert.True(new AppSettings().VrOverlayEnabled);
    }
}
