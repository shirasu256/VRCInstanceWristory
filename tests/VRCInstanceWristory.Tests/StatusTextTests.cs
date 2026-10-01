using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.Logging;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;
using Valve.VR;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// ウィンドウの状態の段の値・色・詳しい文（2026-09-28のユーザー指定→実装メモ5.85）。
/// 利用者が再編集した一覧（状態の段の値・色・詳しい文）どおりに出ることと、同じ段に重なったときの順を確かめる。
/// </summary>
public class StatusTextTests
{
    /// <summary>VRChat が1つ動いていて、ログを読めていて、SteamVR へつながり、手首のパネルを出している状態。</summary>
    private static readonly DesktopStatus Normal = new()
    {
        ClientRunning = true,
        Health = LogHealth.Ok,
        VrConnected = true,
        MenuPageOpen = true,
        VrHideReason = PanelHideReason.None,
        ClientCount = 1,
        VrServerSeen = true,
    };

    private static StatusItem Client(DesktopStatus s) => StatusText.Build(s)[0];

    private static StatusItem Log(DesktopStatus s) => StatusText.Build(s)[1];

    private static StatusItem Vr(DesktopStatus s) => StatusText.Build(s)[2];

    private static StatusItem Wrist(DesktopStatus s) => StatusText.Build(s)[3];

    [Fact]
    public void 正常なときは4項目ともアクセント色で詳しい文を出さない()
    {
        var items = StatusText.Build(Normal);

        Assert.Equal(["VRChat", "VRChat ログ", "SteamVR", "手首パネル"], items.Select(i => i.Label));
        Assert.Equal(["正常動作中", "正常追跡中", "正常動作中", "表示中"], items.Select(i => i.Value));
        Assert.All(items, i => Assert.Equal(StatusTone.Good, i.Tone));
        Assert.All(items, i => Assert.Null(i.Detail));
    }

    // ------------------------------------------------------------------ VRChat

    [Fact]
    public void VRChatの段()
    {
        var stopped = Normal with { ClientRunning = false, ClientCount = 0 };

        Assert.Equal(("未接続", StatusTone.Idle), Pair(Client(stopped)));
        Assert.Equal(("動作中・複数クライアント", StatusTone.Warning), Pair(Client(Normal with { ClientRunning = false, ClientCount = 2 })));
        Assert.Equal(("プロセス取得失敗 (再試行中)", StatusTone.Warning), Pair(Client(stopped with { ProcessListFailing = true })));
        Assert.Equal(("動作中・プロセス情報読取失敗", StatusTone.Warning), Pair(Client(stopped with { ProcessInfoUnreadable = true })));
        Assert.Equal(("異常終了検知", StatusTone.Error), Pair(Client(stopped with { RecentCrash = true })));
        Assert.Equal(("再起動中", StatusTone.Idle), Pair(Client(stopped with { Relaunching = true })));
        Assert.Equal(("起動処理中", StatusTone.Idle), Pair(Client(Normal with { Health = LogHealth.NoLogMatch })));
        Assert.Equal(("動作中・SteamVR以外", StatusTone.Warning), Pair(Client(Normal with { NonSteamVrRuntime = true })));
    }

    [Fact]
    public void クラッシュの表示はVRChatがまた起動したらすぐに譲る()
    {
        Assert.Equal("正常動作中", Client(Normal with { RecentCrash = true }).Value);
    }

    [Fact]
    public void ログを特定できないのが長く続けば起動処理中をやめる()
    {
        var status = Normal with { Health = LogHealth.NoLogMatch, NoLogMatchLong = true };

        Assert.Equal("正常動作中", Client(status).Value);
        Assert.Equal(("追跡失敗・ログ特定不能", StatusTone.Warning), Pair(Log(status)));
    }

    [Fact]
    public void 複数起動はVRChatとログと手首パネルの段に同時に出る()
    {
        // 複数あるときは、エンジンは対応づけをやめるので ClientRunning は false になる。
        var status = Normal with { ClientRunning = false, ClientCount = 2, Health = LogHealth.AmbiguousClient, VrHideReason = PanelHideReason.ContentNotReady };

        Assert.Equal("動作中・複数クライアント", Client(status).Value);
        Assert.Equal("追跡停止・複数クライアント", Log(status).Value);
        Assert.Equal(("非表示・複数クライアント", StatusTone.Warning), Pair(Wrist(status)));

        // 詳しい文は、アプリがどう振る舞っているか（履歴の更新を止めている）を書く。
        Assert.Contains("訪問履歴の更新を止めています", Client(status).Detail);
    }

    // ------------------------------------------------------------------ VRChat ログ

    [Fact]
    public void ログの段()
    {
        Assert.Equal(("初期化中", StatusTone.Idle), Pair(Log(Normal with { Health = LogHealth.Initializing })));
        Assert.Equal(("再構築中", StatusTone.Idle), Pair(Log(Normal with { Health = LogHealth.Rebuilding })));
        Assert.Equal(("再構築中・内部履歴読込失敗", StatusTone.Warning), Pair(Log(Normal with { Health = LogHealth.Rebuilding, CheckpointRebuilt = true })));
        Assert.Equal(("ログ待機中", StatusTone.Idle), Pair(Log(Normal with { Health = LogHealth.NoLogMatch })));
        Assert.Equal(("ログ解析不能", StatusTone.Error), Pair(Log(Normal with { FormatSuspect = true })));
        Assert.Equal(("追跡中・内部履歴再構築", StatusTone.Warning), Pair(Log(Normal with { CheckpointRebuilt = true })));
        Assert.Equal(("追跡中・履歴保存不能", StatusTone.Warning), Pair(Log(Normal with { SaveFailing = true })));
        Assert.Equal(("追跡中・長時間更新停止", StatusTone.Warning), Pair(Log(Normal with { StalledMinutes = 12 })));
        Assert.Equal(("追跡中・時刻異常", StatusTone.Warning), Pair(Log(Normal with { ClockSkewMinutes = 9 })));
        Assert.Equal(("追跡中・一部未読込", StatusTone.Warning), Pair(Log(Normal with { HistoryTruncated = true })));

        Assert.Contains("12 分間", Log(Normal with { StalledMinutes = 12 }).Detail);
        Assert.Contains("9 分", Log(Normal with { ClockSkewMinutes = 9 }).Detail);

        // VRChat が動いていなければ、追うログがないので「待機中」。
        var stopped = Normal with { ClientRunning = false, ClientCount = 0 };
        Assert.Equal(("待機中", StatusTone.Idle), Pair(Log(stopped)));
        Assert.Equal(("ログファイル無し", StatusTone.Idle), Pair(Log(stopped with { LogFolder = LogFolderState.Empty })));
    }

    [Fact]
    public void ログのフォルダーがないときはVRChatが動いているかで赤と灰を分ける()
    {
        var missing = Normal with { LogFolder = LogFolderState.Missing, LogDirectory = @"C:\Users\a\AppData\LocalLow\VRChat\VRChat" };

        var running = Log(missing);
        Assert.Equal(("ログフォルダ到達失敗", StatusTone.Error), Pair(running));
        Assert.Contains(@"C:\Users\a\AppData\LocalLow\VRChat\VRChat", running.Detail);
        Assert.Contains("logDirectory", running.Detail);

        var stopped = Log(missing with { ClientRunning = false, ClientCount = 0 });
        Assert.Equal(("ログフォルダ無し", StatusTone.Idle), Pair(stopped));
        Assert.Contains("VRChat を一度起動すると作られます", stopped.Detail);
    }

    [Theory]
    [InlineData(LogReadFailure.AccessDenied, "読取失敗・アクセス拒否")]
    [InlineData(LogReadFailure.SharingViolation, "読取失敗・他プロセス使用中")]
    [InlineData(LogReadFailure.DriveMissing, "読取失敗・ドライブ未接続")]
    [InlineData(LogReadFailure.Other, "読取失敗 (再試行中)")]
    public void 読めない理由ごとに値を分け長く続けば赤にする(LogReadFailure failure, string value)
    {
        var status = Normal with { Health = LogHealth.ReadError, ReadFailure = failure };

        Assert.Equal((value, StatusTone.Warning), Pair(Log(status)));
        Assert.Equal((value, StatusTone.Error), Pair(Log(status with { ReadFailingLong = true })));
        Assert.Contains("自動で再試行します", Log(status).Detail);
    }

    [Fact]
    public void 読めない例外から理由を分ける()
    {
        Assert.Equal(LogReadFailure.AccessDenied, LogReadFailures.Classify(new UnauthorizedAccessException()));
        Assert.Equal(LogReadFailure.SharingViolation, LogReadFailures.Classify(new IOException("x", unchecked((int)0x80070020))));
        Assert.Equal(LogReadFailure.DriveMissing, LogReadFailures.Classify(new DirectoryNotFoundException()));
        Assert.Equal(LogReadFailure.DriveMissing, LogReadFailures.Classify(new IOException("x", unchecked((int)0x80070015))));
        Assert.Equal(LogReadFailure.Other, LogReadFailures.Classify(new IOException("x")));
    }

    // ------------------------------------------------------------------ SteamVR

    [Theory]
    [InlineData(VrConnectError.TextureDeviceExhausted, "動作中・接続エラー (D3D11デバイス枯渇)", StatusTone.Error)]
    [InlineData(VrConnectError.TextureDevice, "動作中・接続エラー (D3D11デバイス)", StatusTone.Error)]
    [InlineData(VrConnectError.Direct3DUnavailable, "動作中・接続エラー (D3D11使用不能)", StatusTone.Error)]
    [InlineData(VrConnectError.AdapterNotFound, "動作中・接続エラー (GPU取得失敗)", StatusTone.Error)]
    [InlineData(VrConnectError.OpenVrApiMissing, "動作中・接続エラー (openvr_api.dll)", StatusTone.Error)]
    [InlineData(VrConnectError.HeadsetNotFound, "動作中・ヘッドセット未検出", StatusTone.Idle)]
    [InlineData(VrConnectError.RuntimeOutdated, "動作中・接続エラー (SteamVRバージョン)", StatusTone.Error)]
    [InlineData(VrConnectError.InstallationBroken, "動作中・接続エラー (SteamVR整合性)", StatusTone.Error)]
    [InlineData(VrConnectError.Ipc, "動作中・アプリ間通信エラー (再試行中)", StatusTone.Error)]
    [InlineData(VrConnectError.PermissionMismatch, "動作中・権限レベル不一致", StatusTone.Error)]
    [InlineData(VrConnectError.DeviceBusy, "動作中・更新中", StatusTone.Idle)]
    [InlineData(VrConnectError.OverlayUnavailable, "動作中・接続エラー (オーバーレイ)", StatusTone.Error)]
    [InlineData(VrConnectError.GpuReset, "動作中・GPUリセット (再接続中)", StatusTone.Warning)]
    [InlineData(VrConnectError.OverlayKeyInUse, "動作中・描画エラー (オーバーレイ名重複)", StatusTone.Error)]
    [InlineData(VrConnectError.OverlayLimit, "動作中・描画エラー (オーバーレイ数上限)", StatusTone.Error)]
    [InlineData(VrConnectError.OverlayCreate, "動作中・描画エラー (オーバーレイ作成)", StatusTone.Error)]
    public void つなげない理由ごとの値と色(VrConnectError error, string value, StatusTone tone)
    {
        var item = Vr(Normal with { VrConnected = false, VrError = error });

        Assert.Equal((value, tone), Pair(item));
        Assert.NotNull(item.Detail);

        // 画面に出す名前は正式名称だけにする（旧名が残っていない→実装メモ5.84）。
        Assert.DoesNotContain("Instance ID Log Viewer", item.Detail);
        Assert.DoesNotContain("InstanceIDLogger", item.Detail);
    }

    [Fact]
    public void そのほかの初期化の失敗は名前を出す()
    {
        var item = Vr(Normal with { VrConnected = false, VrError = VrConnectError.InitOther, VrErrorName = "Init_Internal" });

        Assert.Equal(("動作中・接続エラー (初期化 Init_Internal)", StatusTone.Error), Pair(item));
        Assert.Contains("Init_Internal", item.Detail);
    }

    [Fact]
    public void つながっていないときはサーバーの有無と終了中を出し分ける()
    {
        var disconnected = Normal with { VrConnected = false };

        Assert.Equal("動作中・接続待機中", Vr(disconnected).Value);
        Assert.Equal("未接続", Vr(disconnected with { VrServerSeen = false }).Value);
        Assert.Equal("終了中", Vr(disconnected with { VrQuitting = true }).Value);
    }

    [Fact]
    public void つながっている間の問題は決めた順で1つだけ出す()
    {
        Assert.Equal(("接続中・例外検知", StatusTone.Warning), Pair(Vr(Normal with { VrIssues = VrRuntimeIssue.FrameErrors | VrRuntimeIssue.TextureRetrying })));
        Assert.Equal(("接続中・描画エラー (テクスチャ作成)", StatusTone.Error), Pair(Vr(Normal with { VrIssues = VrRuntimeIssue.TextureCreateFailed })));
        Assert.Equal(("接続中・描画エラー (再試行中)", StatusTone.Warning), Pair(Vr(Normal with { VrIssues = VrRuntimeIssue.TextureRetrying })));
        Assert.Equal(("接続中・操作不可 (コントローラー割り当て)", StatusTone.Warning), Pair(Vr(Normal with { VrIssues = VrRuntimeIssue.InputUnavailable })));
        Assert.Equal(("接続中・未知コントローラー", StatusTone.Warning), Pair(Vr(Normal with { VrIssues = VrRuntimeIssue.ControllerUnbound })));
        Assert.Equal(("接続中・ダッシュボード未表示", StatusTone.Warning), Pair(Vr(Normal with { VrIssues = VrRuntimeIssue.DashboardUnavailable })));
        Assert.Equal(("接続中・アプリ登録失敗", StatusTone.Warning), Pair(Vr(Normal with { VrIssues = VrRuntimeIssue.ApplicationUnregistered })));
    }

    [Theory]
    [InlineData(EVRInitError.Init_Retry, VrConnectError.None)]
    [InlineData(EVRInitError.Init_NoServerForBackgroundApp, VrConnectError.None)]
    [InlineData(EVRInitError.Init_HmdNotFound, VrConnectError.HeadsetNotFound)]
    [InlineData(EVRInitError.Init_HmdNotFoundPresenceFailed, VrConnectError.HeadsetNotFound)]
    [InlineData(EVRInitError.Init_InterfaceNotFound, VrConnectError.RuntimeOutdated)]
    [InlineData(EVRInitError.Init_InstallationCorrupt, VrConnectError.InstallationBroken)]
    [InlineData(EVRInitError.Init_VRClientDLLNotFound, VrConnectError.InstallationBroken)]
    [InlineData(EVRInitError.IPC_ConnectFailed, VrConnectError.Ipc)]
    [InlineData(EVRInitError.IPC_NamespaceUnavailable, VrConnectError.Ipc)]
    [InlineData(EVRInitError.Init_FirmwareUpdateBusy, VrConnectError.DeviceBusy)]
    [InlineData(EVRInitError.Init_RebootingBusy, VrConnectError.DeviceBusy)]
    [InlineData(EVRInitError.Init_Internal, VrConnectError.InitOther)]
    public void OpenVRの初期化の失敗を理由へ分ける(EVRInitError error, VrConnectError expected)
    {
        Assert.Equal(expected, SteamVrSession.FromInitError(error));
    }

    [Theory]
    [InlineData(EVROverlayError.KeyInUse, VrConnectError.OverlayKeyInUse)]
    [InlineData(EVROverlayError.OverlayLimitExceeded, VrConnectError.OverlayLimit)]
    [InlineData(EVROverlayError.InvalidParameter, VrConnectError.OverlayCreate)]
    public void オーバーレイを作れない理由を分ける(EVROverlayError error, VrConnectError expected)
    {
        Assert.Equal(expected, new OverlayCreateException("k", error).ConnectError);
    }

    [Fact]
    public void SteamVRが終了を知らせてきたあとは残っているサーバーの間だけ終了中にする()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc));
        VrServerProcess? server = new VrServerProcess(100, clock.UtcNow);
        var watcher = new SteamVrWatcher(() => server, () => true, clock);

        Assert.True(watcher.ShouldConnectAtStartup());
        watcher.Connected();

        // SteamVR が終わったあと、サーバーが少し残っている。
        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.False(watcher.ShouldConnect());
        Assert.True(watcher.WaitingForExit);

        server = null;
        clock.Advance(SteamVrWatcher.CheckInterval);
        watcher.ShouldConnect();
        Assert.False(watcher.WaitingForExit);
    }

    [Fact]
    public void GPUのリセットで切ったあとは同じサーバーへ間を置いてつなぎ直す()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc));
        var server = new VrServerProcess(100, clock.UtcNow);
        var watcher = new SteamVrWatcher(() => server, () => true, clock);

        Assert.True(watcher.ShouldConnectAtStartup());
        watcher.Connected();
        watcher.Disconnected();

        // 終了中ではない（SteamVR は動き続けている）。
        Assert.False(watcher.WaitingForExit);

        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.False(watcher.ShouldConnect());

        clock.Advance(SteamVrWatcher.RetryInterval);
        Assert.True(watcher.ShouldConnect());
    }

    [Fact]
    public void OpenXRで動いていてSteamVRのサーバーがなければSteamVR以外とみなす()
    {
        Assert.True(StatusText.IsNonSteamVr("OpenXRLoader", vrServerSeen: false));
        Assert.False(StatusText.IsNonSteamVr("OpenXRLoader", vrServerSeen: true));
        Assert.False(StatusText.IsNonSteamVr("OpenVRLoader", vrServerSeen: false));
        Assert.False(StatusText.IsNonSteamVr(null, vrServerSeen: false));
    }

    // ------------------------------------------------------------------ 手首パネル

    [Fact]
    public void 手首パネルの段()
    {
        Assert.Equal(("SteamVR未接続", StatusTone.Idle), Pair(Wrist(Normal with { VrConnected = false })));
        Assert.Equal(("表示中", StatusTone.Good), Pair(Wrist(Normal)));
        Assert.Equal(("表示中・操作不可 (片手未接続)", StatusTone.Warning), Pair(Wrist(Normal with { OperatingHandMissing = true })));
        Assert.Equal(("非表示・基準角度未満", StatusTone.Quiet), Pair(Wrist(Normal with { VrHideReason = PanelHideReason.ViewAngle })));
        Assert.Equal(("非表示・コントローラー追跡喪失", StatusTone.Warning), Pair(Wrist(Normal with { VrHideReason = PanelHideReason.WristTrackingLost })));
        Assert.Equal(("非表示・コントローラー未接続", StatusTone.Warning), Pair(Wrist(Normal with { VrHideReason = PanelHideReason.ControllerMissing })));
        Assert.Equal(("描画準備中", StatusTone.Idle), Pair(Wrist(Normal with { VrHideReason = PanelHideReason.TextureNotReady })));
        Assert.Equal(("非表示・ヘッドセット未装着", StatusTone.Quiet), Pair(Wrist(Normal with { HeadsetStandby = true })));
        Assert.Equal(("非表示・VRオーバーレイ機能オフ", StatusTone.Quiet), Pair(Wrist(Normal with { VrOverlayEnabled = false })));

        var notReady = Normal with { VrHideReason = PanelHideReason.ContentNotReady };
        Assert.Equal(("非表示・VRChat未起動", StatusTone.Idle), Pair(Wrist(notReady with { ClientRunning = false, ClientCount = 0 })));
        Assert.Equal(("非表示・ログ未追跡", StatusTone.Idle), Pair(Wrist(notReady with { Health = LogHealth.ReadError })));
        Assert.Equal(("非表示・メインメニュー非表示", StatusTone.Quiet), Pair(Wrist(notReady with { MenuPageOpen = false })));
        Assert.Equal(("非表示・訪問履歴無し", StatusTone.Quiet), Pair(Wrist(notReady)));
    }

    // ------------------------------------------------------------------ 時間で出し分ける

    [Fact]
    public void 時間の長さで出し分けるものは主ループの時刻で決める()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

        EngineSnapshot Snapshot(LogHealth health, EngineAlerts alerts) => new()
        {
            ClientRunning = true,
            Presence = Core.Visits.PresenceState.Unknown,
            Health = health,
            History = [],
            MenuPageOpen = true,
            AwaitingViewAngleClose = false,
            BaselineKnown = true,
            CheckpointHealthy = true,
            Generation = 1,
            Alerts = alerts,
        };

        var crash = StatusText.Compose(Snapshot(LogHealth.Ok, new EngineAlerts { CrashDetectedUtc = now.AddSeconds(-59) }), now, null);
        Assert.True(crash.RecentCrash);
        Assert.False(StatusText.Compose(Snapshot(LogHealth.Ok, new EngineAlerts { CrashDetectedUtc = now.AddSeconds(-61) }), now, null).RecentCrash);

        var match = new EngineAlerts { HealthSinceUtc = now - StatusText.NoLogMatchGrace };
        Assert.True(StatusText.Compose(Snapshot(LogHealth.NoLogMatch, match), now, null).NoLogMatchLong);
        Assert.False(StatusText.Compose(Snapshot(LogHealth.NoLogMatch, match with { HealthSinceUtc = now.AddMinutes(-1) }), now, null).NoLogMatchLong);

        var read = new EngineAlerts { HealthSinceUtc = now.AddMinutes(-5) };
        Assert.True(StatusText.Compose(Snapshot(LogHealth.ReadError, read), now, null).ReadFailingLong);

        // ログが増えない時間は10分から出す。分は切り捨て。
        Assert.Equal(0, StatusText.Compose(Snapshot(LogHealth.Ok, new EngineAlerts { LastAppendUtc = now.AddMinutes(-9.9) }), now, null).StalledMinutes);
        Assert.Equal(12, StatusText.Compose(Snapshot(LogHealth.Ok, new EngineAlerts { LastAppendUtc = now.AddMinutes(-12.5) }), now, null).StalledMinutes);

        // 時刻のずれは向きによらず、5分から出す。
        Assert.Equal(0, StatusText.Compose(Snapshot(LogHealth.Ok, new EngineAlerts { ClockSkew = TimeSpan.FromMinutes(4) }), now, null).ClockSkewMinutes);
        Assert.Equal(60, StatusText.Compose(Snapshot(LogHealth.Ok, new EngineAlerts { ClockSkew = TimeSpan.FromMinutes(-60) }), now, null).ClockSkewMinutes);

        Assert.True(StatusText.Compose(Snapshot(LogHealth.Ok, new EngineAlerts { CheckpointUnreadableUtc = now.AddSeconds(-30) }), now, null).CheckpointRebuilt);
    }

    // ------------------------------------------------------------------ エンジン

    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    [Fact]
    public void ログの短縮から読み直したら正常へ戻る()
    {
        // 5.85 より前は、読み直し（Rebuilding）から戻らず、VRChat を起動し直すまで手首のパネルを出さなかった。
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(2), Loc.GroupPublic("222"), "B")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(2).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(3), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.Equal(LogHealth.Ok, harness.Snapshot().Health);

        File.WriteAllText(
            file,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)),
            new System.Text.UTF8Encoding(false));

        for (var second = 10; second <= 40; second += 10)
        {
            harness.SetNow(SessionStart.AddMinutes(3).AddSeconds(second));
            harness.Engine.Update();
        }

        Assert.Equal(LogHealth.Ok, harness.Snapshot().Health);
        Assert.True(harness.Snapshot().ContentReady);
    }

    [Fact]
    public void 動いている間に気づいたクラッシュだけを状態の段へ渡す()
    {
        using var dir = new TempLogDirectory();

        // 前回の起動は終了の記録なく終わっている（起動時の全走査で見つかるクラッシュ）。
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Visit(earlier.AddMinutes(1), Loc.GroupPublic("000"), "Z"));

        // 今回は対象外（ホームなど）にいる。
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.Public("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(10), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.Null(harness.Snapshot().Alerts.CrashDetectedUtc);

        var crashedAt = SessionStart.AddMinutes(30);
        harness.SetNow(crashedAt);
        harness.Processes.Exit(harness.Utc(crashedAt));
        harness.Engine.Update();

        Assert.Equal(harness.Utc(crashedAt), harness.Snapshot().Alerts.CrashDetectedUtc);
    }

    [Fact]
    public void 正常に終わればクラッシュとして渡さない()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.Public("111"), "A"));
        var file = Directory.GetFiles(dir.Path).Single();

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(10), Process(SessionStart));
        harness.Engine.Initialize();

        TempLogDirectory.Append(file, LogText.Quit(SessionStart.AddMinutes(29)));
        harness.SetNow(SessionStart.AddMinutes(30));
        harness.Processes.Exit(harness.Utc(SessionStart.AddMinutes(30)));
        harness.Engine.Update();

        Assert.Null(harness.Snapshot().Alerts.CrashDetectedUtc);
    }

    [Fact]
    public void VRの方式と入室の読み取りの失敗をログから拾う()
    {
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(
            SessionStart,
            LogText.Line(SessionStart.AddSeconds(5), "StartVRSDK: OpenXRLoader")
            + LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        var alerts = harness.Snapshot().Alerts;
        Assert.Equal("OpenXRLoader", alerts.VrSdk);
        Assert.False(alerts.FormatSuspect);

        // 「Joining」の行の形が変わって読めなくなった（入室の確定だけが続く）。
        TempLogDirectory.Append(
            file,
            LogText.Line(SessionStart.AddMinutes(3), "[Behaviour] Joining world wrld_x:1") + LogText.Joined(SessionStart.AddMinutes(3))
            + LogText.Line(SessionStart.AddMinutes(4), "[Behaviour] Joining world wrld_y:2") + LogText.Joined(SessionStart.AddMinutes(4)));

        harness.SetNow(SessionStart.AddMinutes(5));
        harness.Engine.Update();
        Assert.True(harness.Snapshot().Alerts.FormatSuspect);

        // 読める入室があれば戻る。
        TempLogDirectory.Append(file, LogText.Visit(SessionStart.AddMinutes(6), Loc.GroupPublic("222"), "B"));
        harness.SetNow(SessionStart.AddMinutes(7));
        harness.Engine.Update();
        Assert.False(harness.Snapshot().Alerts.FormatSuspect);
    }

    [Fact]
    public void ログのフォルダーの有無とログが増えた時刻を渡す()
    {
        using var dir = new TempLogDirectory();
        var missing = Path.Combine(dir.Path, "none");

        using (var harness = new EngineHarness(missing, SessionStart, process: null))
        {
            harness.Engine.Initialize();
            Assert.Equal(LogFolderState.Missing, harness.Snapshot().Alerts.LogFolder);
        }

        using (var harness = new EngineHarness(dir.Path, SessionStart, process: null))
        {
            harness.Engine.Initialize();
            Assert.Equal(LogFolderState.Empty, harness.Snapshot().Alerts.LogFolder);
        }

        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using (var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart)))
        {
            harness.Engine.Initialize();
            Assert.Equal(LogFolderState.Ok, harness.Snapshot().Alerts.LogFolder);

            TempLogDirectory.Append(file, LogText.Noise(SessionStart.AddMinutes(3)));
            harness.SetNow(SessionStart.AddMinutes(3));
            harness.Engine.Update();

            var alerts = harness.Snapshot().Alerts;
            Assert.Equal(harness.Utc(SessionStart.AddMinutes(3)), alerts.LastAppendUtc);
            Assert.Equal(TimeSpan.Zero, alerts.ClockSkew);
        }
    }

    [Fact]
    public void 主ループが止まっていた間はログが増えない時間に数えない()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();

        // スリープから30分後に戻った。
        harness.SetNow(SessionStart.AddMinutes(32));
        harness.Engine.Update();

        Assert.Equal(harness.Utc(SessionStart.AddMinutes(32)), harness.Snapshot().Alerts.LastAppendUtc);
    }

    // ------------------------------------------------------------------ ウィンドウ

    [Fact]
    public void 詳しい文のある項目を指すと枠を出し段の中に収める()
    {
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetStatus(Normal with { VrConnected = false, VrError = VrConnectError.TextureDeviceExhausted });

        var slot = view.StatusSlot(2);
        view.MouseMove(new PointF(slot.X + 20f, slot.Y + (slot.Height / 2f)));
        Assert.Equal(2, view.PointedStatus);

        // 詳しい文がなく、値も切れていない項目では出さない。
        var client = view.StatusSlot(0);
        view.MouseMove(new PointF(client.X + 20f, client.Y + (client.Height / 2f)));
        Assert.Null(view.PointedStatus);

        // 4項目は重ならない。
        var slots = Enumerable.Range(0, 4).Select(view.StatusSlot).ToList();
        Assert.False(slots[0].IntersectsWith(slots[1]));
        Assert.False(slots[2].IntersectsWith(slots[3]));

        using var bitmap = view.RenderToBitmap();
        Assert.Equal(DesktopView.DefaultClientSize.Width, bitmap.Width);
    }

    [Fact]
    public void 状態の段の値は2つ目の項目にも十分な幅を残す()
    {
        // いちばん長い組み合わせのひとつ。2つ目（手首パネル）は切らずに出す。
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetStatus(Normal with { VrConnected = false, VrError = VrConnectError.Ipc });

        var wrist = view.StatusSlot(3);
        using var painter = new UiPainter(new PanelStyle(), 1f);
        var item = view.StatusItems[3];
        var needed = 7f + 7f + painter.MeasureWidth(item.Label, painter.Fonts.Aux) + 8f + painter.MeasureWidth(item.Value, painter.Fonts.Aux);

        Assert.True(wrist.Width >= needed, $"手首パネルの幅 {wrist.Width} < {needed}");
    }

    private static (string Value, StatusTone Tone) Pair(StatusItem item) => (item.Value, item.Tone);
}
