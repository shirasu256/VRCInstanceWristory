using VRCInstanceWristory.Cli;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Infrastructure.Osc;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// 通常動作の1回の実行。ログ監視とSteamVR表示、デスクトップのウィンドウをつなぐ。
/// 状態更新はこの主ループ（単一スレッド）で順に行い、読み取り・描画・プロセス通知を競合させない。
/// ウィンドウは別のスレッドで動くが、履歴エンジンと設定を触るのはこのループだけにしてある（→実装メモ5.39）。
///
/// ウィンドウ・ダッシュボードからの操作は <c>LiveSession.Commands.cs</c> で処理する。
/// </summary>
public sealed partial class LiveSession : IDisposable
{
    /// <summary>主ループの目標周期（約90Hz）。</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / 90.0);

    /// <summary>SteamVRにつながっていない間の周期。ウィンドウとログの追従だけなので、90Hzで回す必要はない。</summary>
    private static readonly TimeSpan DesktopOnlyFrameInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0);

    /// <summary>フレーム処理の失敗がこの回数続いたら終了する。</summary>
    private const int MaxConsecutiveFrameErrors = 30;

    /// <summary>フレーム処理の失敗が、直近1分にこの回数あれば状態の段に出す（→実装メモ5.85）。</summary>
    private const int FrameErrorNoticeCount = 5;

    private static readonly TimeSpan FrameErrorWindow = TimeSpan.FromMinutes(1);

    /// <summary>フレーム処理に失敗したとき、次を試すまでの間。</summary>
    private static readonly TimeSpan FrameErrorDelay = TimeSpan.FromMilliseconds(200);

    private readonly AppOptions _options;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly string _logDirectory;
    private readonly StartupRegistration _startup;
    private readonly IDiagnostics _log;
    private readonly LogTimeConverter _time = LogTimeConverter.Local;

    // 片付けるもの（Dispose で作ったのと逆の順に片付ける）。
    private readonly PhotoThumbnails _thumbnails;
    private readonly DesktopWindow? _desktop;
    private readonly VRChatProcessMonitor _processes;
    private readonly SessionEndWatcher _sessionEnd;
    private readonly HistoryEngine _engine;
    private readonly ExternalCommandListener _external;

    private readonly SteamVrWatcher _watcher = new(SteamVrWatcher.FindServer, SteamVrWatcher.FindCompositor);
    private readonly PanelTargets _targets;
    private readonly PanelPresenter _presenter;
    private readonly SettingsChanges _changes;
    private readonly FramePacer _pacer = new(FrameInterval);
    private readonly FramePacer _desktopPacer = new(DesktopOnlyFrameInterval);

    /// <summary>SteamVRにつながっている間だけある。</summary>
    private OverlayRuntime? _runtime;

    /// <summary>VRChat の OSC（AFK の状態）の受け口（→実装メモ5.89）。「VRChatのAFKを検知する」がオンのときだけ開く（→実装メモ5.98）。</summary>
    private VrChatOscListener? _osc;

    /// <summary>AFK の受け口を開いたあと、ファイアウォールでブロックされていないかを見張る（→実装メモ5.111）。</summary>
    private readonly AfkFirewallWatch _afkFirewall;

    /// <summary>
    /// 利用者がオンにして見張っている間は、オンにする前の AFK を使う2つの値（ブロックされていたら戻す）。
    /// 起動時に開いたときは null（戻すのは検知だけで、知らせも出さない）。
    /// </summary>
    private (bool Pause, bool Reshow)? _afkBeforeOn;

    /// <summary>
    /// SteamVR は動いているのにつなげなかった理由（ウィンドウの状態に出す→実装メモ5.82・5.85）。
    /// つながったとき・SteamVR のサーバーがいなくなったときに消す。
    /// </summary>
    private VrConnectError _vrError = VrConnectError.None;

    private string? _vrErrorName;

    private volatile bool _stopping;

    /// <summary>新しい版の確認と更新（→実装メモ5.121）。</summary>
    private readonly AppUpdater _updater;

    /// <summary>
    /// 「更新して再起動」で終わるか（→実装メモ5.121）。終わったあとに <see cref="LiveMode"/> が <see cref="ApplyUpdateAfterExit"/> を呼ぶ。
    /// </summary>
    public bool UpdatingOnExit { get; private set; }

    /// <summary>落とした新しい版を、このプロセスが終わったあとで入れ替えるよう頼む（すべての保存を済ませてから呼ぶ）。</summary>
    public void ApplyUpdateAfterExit() => _updater.ApplyAfterExit();
    private bool _lastClientRunning;
    private PanelHideReason _lastHideReason = PanelHideReason.None;
    private long _thumbnailGeneration = -1;
    private int _consecutiveErrors;

    // 状態の段に出すための記録（→実装メモ5.85）。
    private readonly Queue<DateTime> _frameErrors = new();
    private DateTime? _relaunchAt;
    private bool _settingsSaveFailing;

    public LiveSession(AppOptions options, AppSettings settings, string settingsPath, string logDirectory, StartupRegistration startup, IDiagnostics log)
    {
        _options = options;
        _settings = settings;
        _settingsPath = settingsPath;
        _logDirectory = logDirectory;
        _startup = startup;
        _log = log;
        // ブロックかどうかは、このアプリのウィンドウが手前にあるときだけ決める（許可を求める画面が出ている間は決めない→実装メモ5.115）。
        // ウィンドウを出さないときは、手前かどうかを使えないので、いつも手前とみなす。
        _afkFirewall = new AfkFirewallWatch(
            () => WindowsFirewall.Check(Environment.ProcessPath, log),
            () => _desktop is null || DesktopWindow.InFront,
            SystemClock.Instance);

        try
        {
            // 写真のサムネイル（→実装メモ5.55）。見せるのはデスクトップのウィンドウだけなので、ウィンドウを出さないときは作らない
            // （ログのリセットで消えた訪問のものを消すのは、ウィンドウがなくても行う）。
            var showWindow = settings.ShowDesktopWindow && !options.NoWindow;
            _thumbnails = new PhotoThumbnails(AppPaths.Thumbnails, log, generate: showWindow);

            // Windowsの終了の通知（→実装メモ5.59）。ウィンドウのスレッドも同じ通知を受けて待つので、ウィンドウより先に作って渡す。
            // 片付けるのはエンジンの直後（Dispose）。エンジンの保存（チェックポイント）が済んでから、待たせていた終了の通知を返す。
            _sessionEnd = SessionEndWatcher.Start(log);

            // デスクトップのウィンドウ（→実装メモ5.39）。開けなくても、VR側だけで今までどおり動く。
            if (showWindow)
                _desktop = OpenDesktopWindow();

            _targets = new PanelTargets { Desktop = _desktop };
            _presenter = new PanelPresenter(_targets, _time, log) { Groups = settings.GroupNaming };
            _changes = new SettingsChanges(settings, settingsPath, log);

            ConnectAtStartup();

            _processes = new VRChatProcessMonitor(diagnostics: log);

            _engine = new HistoryEngine(CreateEngineOptions(), SystemClock.Instance, _time, _processes, log,
                new CheckpointStore(AppPaths.Checkpoint), new MarkFile(AppPaths.Marks), new HistoryFile(AppPaths.History));
            _engine.Initialize();

            // 外部からの履歴リセットのコマンドの受け口（→実装メモ5.83）。受け取ったものは主ループで処理する。
            _external = new ExternalCommandListener(ExternalCommandListener.DefaultPipeName, log);
            _external.SetEnabled(settings.ExternalResetEnabled);

            // 新しい版の確認（→実装メモ5.121）。インストーラーで入れた版でなければ、何もしない。
            _updater = new AppUpdater(CreateUpdateBackend(log), SystemClock.Instance, log, settings.UpdateCheckEnabled);
            settings.Update = _updater.Status;

            SyncOscListener();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>主ループを回す。終わるときは、落ち着いていない設定の保存と AFK で止めていた分の記録を必ず済ませる。</summary>
    public int Run()
    {
        Console.CancelKeyPress += OnCancelKeyPress;

        // 起動した時点でつながっていれば、「SteamVRと一緒に起動する」の状態をウィンドウへ知らせる（→実装メモ5.50）。
        PublishStartupState();

        var exitCode = ExitCode.Success;

        try
        {
            while (!_stopping)
            {
                try
                {
                    if (!RunFrame())
                        break;

                    _consecutiveErrors = 0;
                }
                catch (Exception ex)
                {
                    if (!RecoverFromFrameError(ex))
                    {
                        exitCode = ExitCode.Failure;
                        break;
                    }

                    Thread.Sleep(FrameErrorDelay);
                    continue;
                }

                (_runtime is not null ? _pacer : _desktopPacer).Wait();
            }

            // フレーム処理の失敗で終わるときも含め、どの終わり方でも行う。
            FinishBeforeExit();
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            DisposeVr();
        }

        if (exitCode == ExitCode.Success)
            Console.WriteLine("終了します。オーバーレイを破棄して状態を保存します。");

        return exitCode;
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _stopping = true;
    }

    /// <summary>
    /// 1フレームぶんの処理。終わるべきとき（Windowsの終了・ウィンドウを閉じた・SteamVRと一緒に終わる）は false。
    /// </summary>
    private bool RunFrame()
    {
        if (_sessionEnd.Requested)
        {
            _log.Notice("Windowsが終了するので、状態を保存して終了します。");
            return false;
        }

        if (_desktop is { Closed: true })
        {
            _log.Notice("デスクトップのウィンドウが閉じられました。");
            return false;
        }

        if (!FollowSteamVr())
            return false;

        // VRChat の AFK（OSC→実装メモ5.89）。VRChat が動いていない間は AFK ではない。
        // 「AFK中はカウントダウンを停止する」がオンなら、AFK の間エンジンのカウントダウンを止める。
        var afk = _osc is { Afk: true } && _lastClientRunning;
        _engine.SetAfkPaused(_settings.AfkPauseActive && afk);

        _engine.Update();

        if (_afkFirewall.Poll())
            RevertBlockedAfk();

        if (_runtime is not null)
            TakePanelOperations(_runtime);

        // ウィンドウでの操作（→5.39）と、SteamVRのダッシュボードでの操作（→5.40）。
        // VR内での操作と同じものは同じ経路でエンジンへ渡す。
        while (_desktop is not null && _desktop.TryTakeCommand(out var command))
            HandleCommand(command, SettingsEditor.Desktop);

        while (_runtime?.Dashboard is { } dashboard && dashboard.TryTakeCommand(out var command))
            HandleCommand(command, SettingsEditor.Dashboard);

        TakeExternalCommands();

        // 新しい版の確認と更新（→実装メモ5.121）。状態が変わったら設定の画面へ知らせる。
        // 落とし終えても、ログの読み込み（初期化・読み直し）が終わるまでは入れ替えない。
        var logReady = _engine.Health is not (LogHealth.Initializing or LogHealth.Rebuilding);

        if (_updater.Poll(_settings.UpdateCheckEnabled, logReady) is { } update)
        {
            _settings.Update = update;
            BroadcastSettings();
        }

        // 手首のパネルの下部にも、新しいバージョンがあることを出す（→実装メモ5.122）。
        if (_runtime is not null)
            _runtime.Controller.UpdateAvailable = _updater.Status.Offering;

        // 「更新して再起動」で落とし終えたら、保存を済ませて終わる（入れ替えて起動し直すのは Velopack）。
        if (_updater.TakeApplyNow(logReady))
        {
            _log.Notice("新しい版を落とし終えたので、状態を保存して終了します。");
            UpdatingOnExit = true;
            return false;
        }

        // 「直前のリセットを戻す」を押せるかは、リセット（手・外部・自動）と戻したときに変わる（→実装メモ5.86）。
        if (_engine.CanUndoClearHistory != _settings.UndoResetAvailable)
        {
            _settings.UndoResetAvailable = _engine.CanUndoClearHistory;
            BroadcastSettings();
        }

        // 保持時間・記録する種類は、値が落ち着いてからエンジンへ渡して保存する。
        _changes.Flush(_engine, force: false);

        if (_changes.TakeSaveResult() is { } saved)
            _settingsSaveFailing = !saved;

        var snapshot = _engine.Snapshot();

        if (_lastClientRunning && !snapshot.ClientRunning)
            _osc?.ResetState();

        _lastClientRunning = snapshot.ClientRunning;

        // 予告のアイコンの設定と、利用者がいまそこにいないか（AFK・ダッシュボード→実装メモ5.89）。
        _presenter.ResetWarningEnabled = _settings.VrOverlayEnabled && _settings.ResetWarningEnabled;
        _presenter.ResetWarningLead = _settings.ResetWarningLead;
        _presenter.ResetWarningReshow = _settings.ResetWarningReshowActive;
        _presenter.ResetWarningBlinkDuration = ResetWarningBlink.DurationFor(_settings.ResetWarningBlinkCount);
        _presenter.Away = afk || (_runtime?.DashboardVisible ?? false);
        _presenter.Apply(snapshot);

        // 履歴にある写真をサムネイルの置き場所へ知らせる（同じ一覧なら何もしない→実装メモ5.55）。
        if (snapshot.Generation != _thumbnailGeneration)
        {
            _thumbnailGeneration = snapshot.Generation;
            _thumbnails.Sync(PhotosNewestFirst(snapshot.History));
        }

        _runtime?.Frame(_presenter.ContentReady);

        if (_runtime is not null)
            FollowPanel(_runtime, snapshot);

        if (_desktop is not null)
            PublishDesktopStatus(_desktop, snapshot);

        return true;
    }

    /// <summary>
    /// 1フレームの失敗でVRChatのプレイ中にアプリごと落とさない。安全側（非表示）へ倒し、原因をPC側へ出して続行する。
    /// 続くようなら諦める（false）。
    /// </summary>
    private bool RecoverFromFrameError(Exception ex)
    {
        _consecutiveErrors++;
        _frameErrors.Enqueue(SystemClock.Instance.UtcNow);
        _log.Error($"フレーム処理でエラー: {ex.GetType().Name}: {ex.Message}");
        _log.Info(ex.ToString());

        _runtime?.Controller.HideAll();

        if (_consecutiveErrors < MaxConsecutiveFrameErrors)
            return true;

        Console.Error.WriteLine($"エラーが{_consecutiveErrors}回続いたため終了します。");
        return false;
    }

    /// <summary>終わる直前に行う。どちらも失敗しても、SteamVR の片付けへ進めるよう例外は外へ出さない。</summary>
    private void FinishBeforeExit()
    {
        try
        {
            // 終了の直前に変えた設定も保存する。
            _changes.Flush(_engine, force: true);

            // AFK でカウントダウンを止めていたら、止めていた分を残してから終わる（→実装メモ5.89）。
            _engine.SetAfkPaused(false);
        }
        catch (Exception ex)
        {
            _log.Error($"終了の前の保存でエラー: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ 起動の準備

    /// <summary>Velopack の更新の仕組み。作れなければ（インストーラーで入れた版でないなど）null。</summary>
    private static IUpdateBackend? CreateUpdateBackend(IDiagnostics log)
    {
        try
        {
            return new VelopackUpdateBackend();
        }
        catch (Exception ex)
        {
            log.Info($"アップデートの仕組みを使えません: {ex.Message}");
            return null;
        }
    }

    private DesktopWindow? OpenDesktopWindow()
    {
        // まだ初回起動の案内と初期設定を終えていなければ、ウィンドウの中身の代わりに案内を出す（2026-09-30のユーザー指定→実装メモ5.97・5.98）。
        // タスクトレイで始めたときは、利用者がウィンドウを開いたときに見える。
        var showWelcome = !_settings.WelcomeCompleted;
        var desktop = new DesktopWindow(OverlayRuntime.CreateStyle(_settings), DesktopSettings.From(_settings), _log, startInTray: _options.Minimized, _thumbnails, showWelcome, _sessionEnd);

        if (desktop.Start(out var windowError))
            return desktop;

        _log.Error($"デスクトップのウィンドウを開けません（VR側だけで続けます）: {windowError}");
        desktop.Dispose();
        return null;
    }

    private EngineOptions CreateEngineOptions()
    {
        return new EngineOptions
        {
            LogDirectory = _logDirectory,
            ProcessMatchBefore = TimeSpan.FromSeconds(_settings.ProcessMatchBeforeSeconds),
            ProcessMatchAfter = TimeSpan.FromSeconds(_settings.ProcessMatchAfterSeconds),
            Retention = _settings.Retention,
            AutoReset = _settings.AutoResetEnabled,
            TargetTypes = _settings.TargetTypes,
            ShowDuringLoadingScreen = _settings.ShowPanelDuringLoading,
            StopCountdownInTarget = _settings.StopCountdownInTarget,
        };
    }

    // ------------------------------------------------------------------ SteamVR

    private void ConnectAtStartup()
    {
        if (!_watcher.ShouldConnectAtStartup())
        {
            // SteamVR がまだ動いていない。ここで OpenVR を初期化すると SteamVR そのものが起動してしまうので、
            // つながずに待つ。SteamVR が起動したら見張りがつなぐ。手で起動したときも同じで、
            // このアプリを開いただけで SteamVR まで立ち上げない（2026-09-27のユーザー指定→実装メモ5.57）。
            // サーバーはいてもコンポジターがいなければ、つながずに待つ（→実装メモ5.88）。
            Console.WriteLine(_watcher.ServerSeen
                ? "SteamVRのコンポジター（vrcompositor）が動いていません。動き出したら自動でつなぎます。"
                : "SteamVRはまだ起動していません。起動したら自動でつなぎます。");
            return;
        }

        if (TryConnect(out var error))
            return;

        // 起動の途中でまだ受け付けないことがある。見張りが間を置いて試し直す。
        Console.Error.WriteLine(error);
        Console.Error.WriteLine(_desktop is null
            ? "SteamVRへまだつなげません。間を置いて試し直します（Ctrl+C で終了）。"
            : "デスクトップのウィンドウだけで動作を続けます。間を置いてつなぎ直します。");
    }

    /// <summary>
    /// SteamVRへつなぐ。SteamVRのサーバーが動いているのを見張りが確かめてから呼ぶ（→実装メモ5.51・5.57）。
    /// つなげなければ、その原因を覚えて見張りに試し直させる。
    /// </summary>
    private bool TryConnect(out string error)
    {
        var runtime = new OverlayRuntime(_settings, _log);

        if (!runtime.Initialize(out error))
        {
            _vrError = RefineConnectError(runtime.ConnectError, _watcher.ServerPid);
            _vrErrorName = runtime.ConnectErrorName;
            runtime.Dispose();
            _watcher.Failed();
            return false;
        }

        _runtime = runtime;
        _vrError = VrConnectError.None;
        _vrErrorName = null;
        _watcher.Connected();
        _targets.Vr = runtime;

        // つないだばかりのパネルには、まだ行も残り時間も渡っていない。
        _presenter.Resend();
        return true;
    }

    /// <summary>
    /// SteamVR の終了・起動・故障に合わせて、切る・つなぐ（→実装メモ5.50・5.51・5.57・5.85・5.88）。
    /// SteamVR と一緒に終わるときは false。
    /// </summary>
    private bool FollowSteamVr()
    {
        if (_runtime is not null && !_runtime.PollEvents())
        {
            // SteamVRの終了。ウィンドウがあれば、そちらだけで続ける。
            // SteamVRが起動のついでに開いたときは、SteamVRと一緒に終わる（→実装メモ5.50）。
            if (_desktop is null || _options.FromSteamVr)
            {
                _log.Notice("SteamVRが終了したので終了します。");
                return false;
            }

            Disconnect(DisconnectReason.SteamVrQuit);
        }

        // SteamVRが起動したら、自分からつなぐ（→実装メモ5.51・5.57）。ウィンドウを出さない設定でも同じ。
        if (_runtime is null && _watcher.ShouldConnect())
        {
            if (TryConnect(out var error))
            {
                _log.Notice("SteamVRが起動したので接続しました。");

                // 「SteamVRと一緒に起動する」は、つないで初めて分かる（→実装メモ5.50）。
                PublishStartupState();
            }
            else
            {
                _log.Info($"SteamVRへまだつなげません（あとで試し直します）: {error}");
            }
        }

        if (_runtime is null && !_watcher.ServerSeen)
            _vrError = VrConnectError.None;

        // GPU がリセットされると、作った D3D11 デバイスは使えなくなる（→実装メモ5.85）。
        // SteamVR は動き続けているので、いったん切って、間を置いてつなぎ直す。
        if (_runtime is not null && _runtime.TextureDeviceLost)
            Disconnect(DisconnectReason.DeviceLost);

        // コンポジターが落ちると、サーバーにはつながったままでもオーバーレイは消えている（→実装メモ5.88）。
        // 「正常動作中」と出し続けないよう、いったん切る。コンポジターが動き出したら見張りがつなぎ直す。
        if (_runtime is not null && _watcher.CompositorLost())
            Disconnect(DisconnectReason.CompositorLost);

        return true;
    }

    /// <summary>SteamVR から切る理由。</summary>
    private enum DisconnectReason
    {
        /// <summary>SteamVR が終了した。同じサーバーへはつなぎ直さない（終了の途中で残っているだけかもしれない）。</summary>
        SteamVrQuit,

        /// <summary>GPU がリセットされ、D3D11 デバイスが失われた。</summary>
        DeviceLost,

        /// <summary>SteamVR のコンポジターが終了した。</summary>
        CompositorLost,
    }

    /// <summary>SteamVR から切って、デスクトップのウィンドウだけで続ける。</summary>
    private void Disconnect(DisconnectReason reason)
    {
        switch (reason)
        {
            case DisconnectReason.SteamVrQuit:
                _log.Notice("SteamVRが終了しました。デスクトップのウィンドウだけで動作を続けます。");
                break;

            case DisconnectReason.DeviceLost:
                _log.Warn("GPU がリセットされ、D3D11デバイスが失われました。SteamVRへつなぎ直します。");
                break;

            case DisconnectReason.CompositorLost:
                _log.Warn("SteamVRのコンポジター（vrcompositor）が終了しました。コンポジターが動き出すまで、デスクトップのウィンドウだけで動作を続けます。");
                break;
        }

        DisposeVr(osc: false);
        _targets.Vr = null;

        // SteamVR は動き続けているので、同じサーバーへも間を置いてつなぎ直す。
        if (reason != DisconnectReason.SteamVrQuit)
        {
            _vrError = reason == DisconnectReason.DeviceLost ? VrConnectError.GpuReset : VrConnectError.None;
            _vrErrorName = null;
            _watcher.Disconnected();
        }

        // つながっていない間は、SteamVR側の登録は分からない。
        _settings.LaunchWithSteamVr = null;
        PublishStartupState();
    }

    /// <summary>
    /// SteamVR との通信に失敗したとき、SteamVR のサーバーが管理者として動いていて、このアプリがそうでなければ、
    /// 「権限レベル不一致」に言い換える（→実装メモ5.85）。
    /// </summary>
    private static VrConnectError RefineConnectError(VrConnectError error, int? serverPid)
    {
        if (error != VrConnectError.Ipc || serverPid is not { } pid)
            return error;

        return ProcessElevation.IsElevated(pid) == true && !ProcessElevation.CurrentIsElevated()
            ? VrConnectError.PermissionMismatch
            : error;
    }

    /// <summary>手首のパネルでの操作（→5.32・5.43・5.53・5.65）を受け取る。</summary>
    private void TakePanelOperations(OverlayRuntime runtime)
    {
        // B / Y ボタンの短押し（0.1秒以内に離す）でパネルを閉じる。
        // メインメニューを閉じたログでも閉じる（→5.104）ので、どちらで閉じたかを --verbose で見分けられるようにする。
        if (runtime.Controller.TakeClosePressed())
        {
            _log.Info("B / Y の短押しを受け取りました。");
            _engine.ClosePanel();
        }

        // 見出しの「延長」を押したら、消去までの時間を数え直す。
        if (runtime.Controller.TakeResetPressed())
            _engine.ResetRetention();

        // 見出しの「リセット」を押して確認で「リセット」を選んだら、訪問履歴を今すぐ消す（→5.65）。
        if (runtime.Controller.TakeClearPressed())
            _engine.ClearHistory();

        // 行のポップアップで目印を選んだら、そのインスタンスへ付け外しする（→5.32）。
        if (runtime.Controller.TakeMarkRequest() is { } mark)
            _engine.SetMark(mark.EventId, mark.Mark);

        // 行のポップアップで「ブラウザで開く」／「ここへ戻る」を選んだら、設定の開き方で開く（→5.43・5.53）。
        if (runtime.Controller.TakeLaunchRequest() is { } launch)
            OpenInstance(launch);
    }

    /// <summary>パネルの状態に合わせて、閉じる・配置を保存する・表示の変化を記録する。</summary>
    private void FollowPanel(OverlayRuntime runtime, EngineSnapshot snapshot)
    {
        // ロード画面を抜けた後、初めて手首の角度で隠れた時点で閉じる
        // （2026-09-21・2026-09-22のユーザー指定）。それ以外のときはエンジン側で何も起きない。
        if (snapshot.AwaitingViewAngleClose && runtime.Controller.ViewAngleHidden)
            _engine.CloseByViewAngle();

        // 使用中でも位置を直せる。掴んで離した時点の配置を設定へ書き戻す。
        if (runtime.Controller.TakeGrabReleased())
        {
            var side = runtime.Controller.Wrist;
            _settings.SetPlacement(side, runtime.Controller.Translation, runtime.Controller.Rotation);
            SavePlacement(side);
        }

        // ちらつきの追跡用。非表示の理由が変わったときだけ記録する。
        if (runtime.Controller.LastHideReason != _lastHideReason)
        {
            _lastHideReason = runtime.Controller.LastHideReason;
            _log.Info($"パネルの表示状態: {(_lastHideReason == PanelHideReason.None ? "表示" : $"非表示（{_lastHideReason}）")}");
        }
    }

    // ------------------------------------------------------------------ 画面へ知らせる

    /// <summary>
    /// SteamVR・Windows の登録の状態を、両方の設定の画面へ知らせる（→実装メモ5.50・5.51）。
    /// 変えた側にも返す（登録できなかったときに、押した見た目のままにしないため）。
    /// </summary>
    private void PublishStartupState()
    {
        _desktop?.SetStartupState(_settings.LaunchWithSteamVr, _settings.LaunchAtLogon);
        _runtime?.Dashboard?.SetStartupState(_settings.LaunchWithSteamVr, _settings.LaunchAtLogon);
    }

    /// <summary>
    /// いまの設定を設定の画面（ウィンドウとダッシュボード）へ知らせる。<paramref name="except"/> には送らない
    /// （変えた側は自分の写しを既に持っていて、送り返すと続けて押したときに1つ前の値へ戻って見える→実装メモ5.40）。
    /// </summary>
    private void BroadcastSettings(SettingsEditor? except = null)
    {
        var current = DesktopSettings.From(_settings);

        if (except != SettingsEditor.Desktop)
            _desktop?.SetSettings(current);

        if (except != SettingsEditor.Dashboard)
            _runtime?.Dashboard?.SetSettings(current);
    }

    /// <summary>ウィンドウの状態の段へ知らせる（→実装メモ5.82・5.85）。</summary>
    private void PublishDesktopStatus(DesktopWindow desktop, EngineSnapshot snapshot)
    {
        var now = SystemClock.Instance.UtcNow;

        while (_frameErrors.Count > 0 && now - _frameErrors.Peek() > FrameErrorWindow)
            _frameErrors.Dequeue();

        // 「ここへ戻る」で起動し直している間（新しい VRChat が起動するまで。長くても2分）。
        if (_relaunchAt is { } launched
            && (now - launched > StatusText.RelaunchLimit || snapshot.Alerts.ClientStartUtc > launched))
        {
            _relaunchAt = null;
        }

        var status = StatusText.Compose(snapshot, now, _logDirectory);
        var issues = _runtime?.Issues ?? VrRuntimeIssue.None;

        if (_frameErrors.Count >= FrameErrorNoticeCount)
            issues |= VrRuntimeIssue.FrameErrors;

        desktop.SetStatus(status with
        {
            VrConnected = _runtime is not null,
            VrHideReason = _runtime?.Controller.LastHideReason ?? PanelHideReason.ContentNotReady,

            // コンポジターがいないことは、つなげなかった理由より先に出す（どの理由も、コンポジターが戻るまで直らない）。
            VrError = _runtime is not null ? VrConnectError.None
                : _watcher.CompositorDown ? VrConnectError.CompositorDown
                : _vrError,
            VrErrorName = _runtime is null ? _vrErrorName : null,
            VrServerSeen = _runtime is not null || _watcher.ServerSeen,
            VrQuitting = _runtime is null && _watcher.WaitingForExit,
            VrIssues = issues,
            Relaunching = _relaunchAt is not null,

            // 「ここへ戻る」で起動し直している間は、古いクライアントの終わり方をクラッシュとして出さない。
            RecentCrash = status.RecentCrash && _relaunchAt is null,
            NonSteamVrRuntime = _runtime is null && StatusText.IsNonSteamVr(snapshot.Alerts.VrSdk, _watcher.ServerSeen),
            SaveFailing = status.SaveFailing || _settingsSaveFailing,
            VrOverlayEnabled = _settings.VrOverlayEnabled,
            HeadsetStandby = _runtime?.HeadsetStandby ?? false,
            OperatingHandMissing = _runtime?.Controller.OperatingHandMissing ?? false,

            // 新しい版があれば、状態の段の右端で知らせる（→実装メモ5.121）。
            UpdateVersion = _updater.Status is { Offering: true } offer ? offer.Version : null,
            UpdateProgress = _updater.Status is { Phase: UpdatePhase.Downloading or UpdatePhase.Ready or UpdatePhase.Waiting } downloading ? downloading.Progress : null,
        });
    }

    // ------------------------------------------------------------------ そのほか

    /// <summary>VRChat の OSC の受け口を、設定に合わせて開く・閉じる（→実装メモ5.89・5.98）。</summary>
    private void SyncOscListener()
    {
        if (_settings.NeedsAfkState && _osc is null)
        {
            _osc = new VrChatOscListener(_log);

            if (!_osc.Start())
            {
                _osc.Dispose();
                _osc = null;
                return;
            }

            // 受け口を開けても、ファイアウォールでブロックされていれば外からは届かない（→実装メモ5.111）。
            _afkFirewall.Start();
        }
        else if (!_settings.NeedsAfkState && _osc is not null)
        {
            _afkFirewall.Stop();
            _afkBeforeOn = null;
            _osc.Dispose();
            _osc = null;
            _log.Info("VRChatのAFKの検知をオフにしたので、OSC の受け口を閉じました。");
        }
    }

    /// <summary>履歴にある写真（新しい訪問・新しい写真から。サムネイルを先に作る順）。</summary>
    private static List<string> PhotosNewestFirst(IReadOnlyList<VisitRecord> history)
    {
        var photos = new List<string>();

        for (var i = history.Count - 1; i >= 0; i--)
        {
            for (var j = history[i].Photos.Count - 1; j >= 0; j--)
                photos.Add(history[i].Photos[j].Path);
        }

        return photos;
    }

    /// <summary>SteamVR のオーバーレイ（と、求めれば OSC の受け口）を片付ける。</summary>
    private void DisposeVr(bool osc = true)
    {
        _runtime?.Dispose();
        _runtime = null;

        if (osc)
        {
            _osc?.Dispose();
            _osc = null;
        }
    }

    public void Dispose()
    {
        DisposeVr();

        // 作ったのと逆の順。エンジン（チェックポイントの保存）の後で、Windowsの終了の通知を返す（→実装メモ5.59）。
        _external?.Dispose();
        _engine?.Dispose();
        _sessionEnd?.Dispose();
        _processes?.Dispose();
        _desktop?.Dispose();
        _thumbnails?.Dispose();
    }
}
