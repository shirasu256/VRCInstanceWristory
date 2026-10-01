using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// VRChat の AFK を使う2つの設定（AFK 中はカウントダウンを止める・予告通知）と、予告通知と手首パネルの設定の部品
/// （2026-09-29のユーザー指定→実装メモ5.89）。AFK を OSC・OSCQuery で受け取るところは <see cref="VrChatOscTests"/> で確かめる。
/// </summary>
public class AfkTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    // ---------------------------------------------------------------- AFK中はカウントダウンを停止する

    [Fact]
    public void AFKの間はカウントダウンが止まり_戻ると止めた時点の残りから数える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(leftAt));

        using var harness = new EngineHarness(dir.Path, leftAt.AddMinutes(10), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();

        // 退出から10分。残り50分で AFK になる。
        harness.Engine.SetAfkPaused(true);
        Assert.True(harness.Snapshot().CountdownStopped);
        Assert.Equal(TimeSpan.FromMinutes(50), harness.Snapshot().RetentionRemaining(harness.Utc(leftAt.AddMinutes(10))));

        // AFK のまま2時間。期限（退出+60分）を過ぎても消えず、残りは50分のまま。
        var later = leftAt.AddMinutes(130);
        harness.SetNow(later);
        harness.Engine.Update();

        Assert.Single(harness.Snapshot().History);
        Assert.Equal(TimeSpan.FromMinutes(50), harness.Snapshot().RetentionRemaining(harness.Utc(later)));

        // 戻る。止めた時点の残り50分から数え直す。
        harness.Engine.SetAfkPaused(false);
        Assert.False(harness.Snapshot().CountdownStopped);
        Assert.Equal(harness.Utc(later.AddMinutes(50)), harness.Snapshot().RetentionDeadlineUtc);

        harness.SetNow(later.AddMinutes(49));
        harness.Engine.Update();
        Assert.Single(harness.Snapshot().History);

        harness.SetNow(later.AddMinutes(50));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);
    }

    [Fact]
    public void AFKで止めていた分は再起動しても失われない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(leftAt));
        using var tempFile = new TempFile("checkpoint");
        var path = tempFile.Path;

        var back = leftAt.AddMinutes(200);

        using (var first = new EngineHarness(dir.Path, leftAt.AddMinutes(40), Process(SessionStart), new CheckpointStore(path)))
        {
            first.Engine.Initialize();
            first.Engine.Update();

            // 残り20分で AFK、160分後に戻る。
            first.Engine.SetAfkPaused(true);
            first.SetNow(back);
            first.Engine.Update();
            first.Engine.SetAfkPaused(false);
            Assert.Equal(first.Utc(back.AddMinutes(20)), first.Snapshot().RetentionDeadlineUtc);
        }

        using var second = new EngineHarness(dir.Path, back.AddMinutes(10), Process(SessionStart), new CheckpointStore(path));
        second.Engine.Initialize();
        second.Engine.Update();

        Assert.Single(second.Snapshot().History);
        Assert.Equal(second.Utc(back.AddMinutes(20)), second.Snapshot().RetentionDeadlineUtc);
    }

    [Fact]
    public void 滞在中のAFKでは何も変えない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(5), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();

        harness.Engine.SetAfkPaused(true);
        harness.SetNow(SessionStart.AddMinutes(90));
        harness.Engine.Update();
        harness.Engine.SetAfkPaused(false);

        Assert.Null(harness.Snapshot().RetentionDeadlineUtc);
        Assert.Single(harness.Snapshot().History);
    }

    // ---------------------------------------------------------------- 設定の部品

    private sealed class Fixture : IDisposable
    {
        // 予告通知の部品を押して確かめるので、既定（オフ）ではなく「リセット予告アイコンを表示する」をオンにした状態で始める。
        // AFK を使う部品は「VRChatのAFKを検知する」がオンのときだけ押せる（→実装メモ5.98）ので、それもオンにする。
        public Fixture(DesktopSettings? settings = null, SettingsSections sections = SettingsSections.All)
        {
            View = new SettingsView(new PanelStyle(), settings ?? DesktopSettings.From(new AppSettings { ResetWarningEnabled = true, AfkDetectionEnabled = true }), c => Commands.Add(c));
            View.Layout(new PointF(0f, 0f), 1f, columns: 1, sections);
        }

        public SettingsView View { get; }

        public List<DesktopCommand> Commands { get; } = [];

        public void Click(SettingsView.HitKind kind, int index = 0)
        {
            var rect = View.TargetRect(kind, index)!.Value;
            View.PointerDown(new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f)));
            View.PointerUp();
        }

        public DesktopCommand.ChangeSettings Last => Assert.IsType<DesktopCommand.ChangeSettings>(Commands[^1]);

        public void Dispose() => View.Dispose();
    }

    [Fact]
    public void 予告通知の部品は上から表示する_位置_サイズ_不透明度_回数_タイミング_再表示の順()
    {
        using var f = new Fixture();
        var first = SettingsView.WarningStepperFirst;

        var tops = new[]
        {
            f.View.TargetRect(SettingsView.HitKind.ResetWarning)!.Value.Y,
            f.View.TargetRect(SettingsView.HitKind.StepperPlus, first)!.Value.Y,
            f.View.TargetRect(SettingsView.HitKind.StepperPlus, first + 1)!.Value.Y,
            f.View.TargetRect(SettingsView.HitKind.StepperPlus, first + 2)!.Value.Y,
            f.View.TargetRect(SettingsView.HitKind.StepperPlus, first + 3)!.Value.Y,
            f.View.TargetRect(SettingsView.HitKind.StepperPlus, first + 4)!.Value.Y,
            f.View.TargetRect(SettingsView.HitKind.WarningReshow)!.Value.Y,
        };

        Assert.Equal(tops.Order(), tops);
        Assert.Equal("左下", f.View.StepperText(first));
        Assert.Equal("100 %", f.View.StepperText(first + 1));
        Assert.Equal("100 %", f.View.StepperText(first + 2));
        Assert.Equal("5 回", f.View.StepperText(first + 3));
        Assert.Equal("3 分前", f.View.StepperText(first + 4));

        // VR オーバーレイ設定のタブ（手首パネルの下）に出る。
        Assert.True(SettingsSections.Dashboard.HasFlag(SettingsSections.ResetWarning));
        Assert.True(f.View.TargetRect(SettingsView.HitKind.ResetWarning)!.Value.Y > f.View.TargetRect(SettingsView.HitKind.ResetWristParameters)!.Value.Y);
    }

    [Fact]
    public void 表示位置は右で左下から巡り_左で逆に巡る()
    {
        using var f = new Fixture();
        var index = SettingsView.WarningStepperFirst;

        f.Click(SettingsView.HitKind.StepperPlus, index);
        Assert.Equal(ResetWarningPosition.Left, f.Last.Settings.WarningPosition);
        Assert.Equal(SettingsField.ResetWarning, f.Last.Fields);

        // 左下から ‹ で「下」へ（端がない）。
        using var g = new Fixture();
        g.Click(SettingsView.HitKind.StepperMinus, index);
        Assert.Equal(ResetWarningPosition.Bottom, g.Last.Settings.WarningPosition);
    }

    [Fact]
    public void 予告の数値は決まった刻みで動く()
    {
        using var f = new Fixture();
        var first = SettingsView.WarningStepperFirst;

        f.Click(SettingsView.HitKind.StepperPlus, first + 1);
        Assert.Equal(1.2f, f.Last.Settings.WarningScale);

        f.Click(SettingsView.HitKind.StepperMinus, first + 2);
        Assert.Equal(0.98f, f.Last.Settings.WarningOpacity);

        f.Click(SettingsView.HitKind.StepperPlus, first + 3);
        Assert.Equal(10, f.Last.Settings.WarningBlinkCount);

        f.Click(SettingsView.HitKind.StepperPlus, first + 4);
        Assert.Equal(5, f.Last.Settings.WarningLeadMinutes);

        Assert.All(f.Commands, c => Assert.Equal(SettingsField.ResetWarning, Assert.IsType<DesktopCommand.ChangeSettings>(c).Fields));
    }

    [Fact]
    public void 予告アイコンをオフにするとその下の部品はグレーアウトして押せない()
    {
        using var f = new Fixture(DesktopSettings.From(new AppSettings()) with { ResetWarningEnabled = false });
        var first = SettingsView.WarningStepperFirst;

        f.Click(SettingsView.HitKind.StepperPlus, first + 1);
        f.Click(SettingsView.HitKind.WarningReshow);
        Assert.Empty(f.Commands);

        // 「リセット予告アイコンを表示する」は押せる。
        f.Click(SettingsView.HitKind.ResetWarning);
        Assert.True(f.Last.Settings.ResetWarningEnabled);

        // 既定はオフなので、何も変えていなければその下は押せない（→実装メモ5.90）。
        using var d = new Fixture(DesktopSettings.From(new AppSettings()));
        d.Click(SettingsView.HitKind.StepperPlus, first);
        Assert.Empty(d.Commands);

        // VR オーバーレイ機能をオフにしている間は、表示するも押せない。
        using var g = new Fixture(DesktopSettings.From(new AppSettings()) with { VrOverlayEnabled = false });
        g.Click(SettingsView.HitKind.ResetWarning);
        Assert.Empty(g.Commands);
    }

    [Fact]
    public void AFK中はカウントダウンを停止するは直前のリセットを戻すの上にある()
    {
        using var f = new Fixture();

        var afk = f.View.TargetRect(SettingsView.HitKind.AfkPause)!.Value;
        var undo = f.View.TargetRect(SettingsView.HitKind.UndoClear)!.Value;
        var retention = f.View.TargetRect(SettingsView.HitKind.StepperPlus, 0)!.Value;

        Assert.True(retention.Bottom <= afk.Y);
        Assert.True(afk.Bottom < undo.Y);

        f.Click(SettingsView.HitKind.AfkPause);
        Assert.True(f.Last.Settings.PauseCountdownWhileAfk);
        Assert.Equal(SettingsField.AfkPause, f.Last.Fields);
    }

    [Fact]
    public void 手首パネルのパラメータをデフォルトに戻すは配置と手首以外を戻す()
    {
        var changed = DesktopSettings.From(new AppSettings()) with
        {
            PanelWidthMeters = 0.2f,
            BackgroundOpacity = 0.6f,
            ViewAngleLimitDegrees = 50f,
            ViewAngleFadeSeconds = 1f,
            ScrollRowsPerSecond = 12f,
            ShowDuringLoading = false,
            PanelGrabEnabled = false,
            TriggerMenuEnabled = false,
            Wrist = WristSide.Right,
        };

        using var f = new Fixture(changed);

        // 手首パネルのまとまりの末尾（位置を戻すボタンの下）。
        Assert.True(f.View.TargetRect(SettingsView.HitKind.ResetWristParameters)!.Value.Y > f.View.TargetRect(SettingsView.HitKind.ResetPlacement)!.Value.Y);

        f.Click(SettingsView.HitKind.ResetWristParameters);

        var defaults = DesktopSettings.From(new AppSettings());
        var result = f.Last.Settings;
        Assert.Equal(defaults.PanelWidthMeters, result.PanelWidthMeters);
        Assert.Equal(defaults.BackgroundOpacity, result.BackgroundOpacity);
        Assert.Equal(defaults.ViewAngleLimitDegrees, result.ViewAngleLimitDegrees);
        Assert.Equal(defaults.ViewAngleFadeSeconds, result.ViewAngleFadeSeconds);
        Assert.Equal(defaults.ScrollRowsPerSecond, result.ScrollRowsPerSecond);
        Assert.True(result.ShowDuringLoading && result.PanelGrabEnabled && result.TriggerMenuEnabled);
        Assert.Equal(WristSide.Right, result.Wrist);
        Assert.Equal(SettingsView.WristParameterFields, f.Last.Fields);

        // 設定へ写すと、配置は変わらない。
        var settings = new AppSettings { TranslationMeters = [0.1f, 0.2f, 0.3f] };
        result.ApplyTo(settings, f.Last.Fields);
        Assert.Equal([0.1f, 0.2f, 0.3f], settings.TranslationMeters);
        Assert.Equal(new AppSettings().OverlayWidthMeters, settings.OverlayWidthMeters);
    }

    [Fact]
    public void 非表示にする角度は2刻み()
    {
        using var f = new Fixture();

        // 手首パネルの −／＋ の3つ目（パネルの幅・背景の不透明度・非表示にする角度）。
        f.Click(SettingsView.HitKind.StepperPlus, 3);
        Assert.Equal(32f, f.Last.Settings.ViewAngleLimitDegrees);

        Assert.Equal(34f, SettingsSteppers.StepValue(33f, 2f, 6f, 90f, +1));
        Assert.Equal(6f, SettingsSteppers.StepValue(7f, 2f, 6f, 90f, -1));
    }

    [Fact]
    public void 背景の不透明度は50パーセント未満を受け付けない()
    {
        var log = new CollectingDiagnostics();
        var settings = new AppSettings { BackgroundOpacity = 0.3f };
        settings.Validate(log);

        Assert.Equal(new AppSettings().BackgroundOpacity, settings.BackgroundOpacity);
        Assert.Contains(log.Messages.Where(m => m.StartsWith("WARN", StringComparison.Ordinal)).ToList(), w => w.Contains("backgroundOpacity", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0.5f, SettingsSteppers.OpacitySteps[0]);
    }

    [Fact]
    public void 予告とAFKの設定を保存して読み直せる()
    {
        using var tempFile = new TempFile("checkpoint");
        var path = tempFile.Path;

        var settings = new AppSettings
        {
            ResetWarningEnabled = false,
            WarningPosition = ResetWarningPosition.TopRight,
            ResetWarningScale = 1.4f,
            ResetWarningOpacity = 0.55f,
            ResetWarningBlinkCount = 30,
            ResetWarningLeadMinutes = 1,
            ResetWarningReshowAfterAfk = false,
            PauseCountdownWhileAfk = true,
        };

        settings.SaveFields(path, SettingsField.ResetWarning | SettingsField.AfkPause);

        var log = new CollectingDiagnostics();
        var loaded = AppSettings.Load(path, log);
        loaded.Validate(log);

        Assert.Empty(log.Messages.Where(m => m.StartsWith("WARN", StringComparison.Ordinal)).ToList());
        Assert.False(loaded.ResetWarningEnabled);
        Assert.Equal(ResetWarningPosition.TopRight, loaded.WarningPosition);
        Assert.Equal(1.4f, loaded.ResetWarningScale);
        Assert.Equal(0.55f, loaded.ResetWarningOpacity);
        Assert.Equal(30, loaded.ResetWarningBlinkCount);
        Assert.Equal(1, loaded.ResetWarningLeadMinutes);
        Assert.False(loaded.ResetWarningReshowAfterAfk);
        Assert.True(loaded.PauseCountdownWhileAfk);

        // 受け口を開くかは「VRChatのAFKを検知する」だけで決まる（→実装メモ5.98）。保存したファイルには既定（オフ）が書かれている。
        Assert.False(loaded.AfkDetectionEnabled);
        Assert.False(loaded.NeedsAfkState);
    }

    [Fact]
    public void 選べない値は既定へ戻す()
    {
        var log = new CollectingDiagnostics();
        var settings = new AppSettings
        {
            ResetWarningPosition = "center",
            ResetWarningScale = 3f,
            ResetWarningOpacity = 0.1f,
            ResetWarningBlinkCount = 7,
            ResetWarningLeadMinutes = 10,
        };

        settings.Validate(log);

        Assert.Equal(ResetWarningPosition.BottomLeft, settings.WarningPosition);
        Assert.Equal(1f, settings.ResetWarningScale);
        Assert.Equal(1f, settings.ResetWarningOpacity);
        Assert.Equal(5, settings.ResetWarningBlinkCount);
        Assert.Equal(3, settings.ResetWarningLeadMinutes);
        Assert.Equal(5, log.Messages.Where(m => m.StartsWith("WARN", StringComparison.Ordinal)).ToList().Count);
    }

    [Fact]
    public void AFKの検知をオンにするまで受け口を開かない()
    {
        // 受け口を開くのは「VRChatのAFKを検知する」をオンにしたときだけ（既定はオフ→実装メモ5.98）。
        // それまでは AFK を使う設定がオンでも開かない（起動しただけでファイアウォールの許可を求めないため）。
        Assert.False(new AppSettings().NeedsAfkState);
        Assert.False(new AppSettings { ResetWarningEnabled = true }.NeedsAfkState);
        Assert.False(new AppSettings { ResetWarningEnabled = false, PauseCountdownWhileAfk = true }.NeedsAfkState);
        Assert.True(new AppSettings { AfkDetectionEnabled = true }.NeedsAfkState);
        Assert.True(new AppSettings { AfkDetectionEnabled = true, PauseCountdownWhileAfk = true }.NeedsAfkState);
    }
}
