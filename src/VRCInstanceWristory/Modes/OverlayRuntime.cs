using System.Diagnostics;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Scrolling;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// SteamVR側の初期化とフレーム処理をまとめる。
/// 初期化に失敗した場合はPC側へ原因を出し、VR内には何も表示しない（仕様9.3節）。
/// </summary>
public sealed class OverlayRuntime : IPanelTarget, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IDiagnostics _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly PanelStyle _style;
    private readonly ScrollController _scroll;

    private SteamVrSession? _session;
    private SteamVrInput? _input;
    private PanelRenderer? _renderer;
    private OverlayController? _controller;
    private SettingsDashboard? _dashboard;
    private ResetWarningOverlay? _resetWarning;
    private TimeSpan _lastFrame;

    public OverlayRuntime(AppSettings settings, IDiagnostics log)
    {
        _settings = settings;
        _log = log;
        _style = CreateStyle(settings);

        _scroll = new ScrollController
        {
            RowHeight = _style.RowHeight,
            ViewportHeight = _style.MaxViewportHeight,
            Deadzone = settings.ScrollDeadzone,
            RowsPerSecond = settings.ScrollRowsPerSecond,
        };
    }

    /// <summary><see cref="Initialize"/> でつなげなかった理由のうち、ウィンドウの状態に出すもの（→実装メモ5.82・5.85）。</summary>
    public VrConnectError ConnectError { get; private set; }

    /// <summary><see cref="ConnectError"/> が「そのほかの初期化の失敗」のとき、その名前（<c>EVRInitError</c>）。</summary>
    public string? ConnectErrorName { get; private set; }

    /// <summary>
    /// つながっている間に見つけた、一部が働かない理由（→実装メモ5.85）。フレーム処理の例外は主ループが数えるので、ここには入らない。
    /// </summary>
    public VrRuntimeIssue Issues
    {
        get
        {
            if (_session is null || _controller is null || _input is null)
                return VrRuntimeIssue.None;

            var issues = VrRuntimeIssue.None;

            if (_controller.TextureCreateFailed)
                issues |= VrRuntimeIssue.TextureCreateFailed;

            if (_controller.TextureRetrying)
                issues |= VrRuntimeIssue.TextureRetrying;

            if (!_input.Ready)
                issues |= VrRuntimeIssue.InputUnavailable;
            else if (_controller.ControllerUnbound)
                issues |= VrRuntimeIssue.ControllerUnbound;

            if (_dashboard is not { Failing: false })
                issues |= VrRuntimeIssue.DashboardUnavailable;

            if (!_session.ApplicationRegistered)
                issues |= VrRuntimeIssue.ApplicationUnregistered;

            return issues;
        }
    }

    /// <summary>SteamVR のダッシュボードを開いているか（予告を見逃したかの判断に使う→実装メモ5.89）。</summary>
    public bool DashboardVisible => _session?.DashboardVisible ?? false;

    /// <summary>作った D3D11 デバイスが失われたか（GPU のリセット→実装メモ5.85）。失われていれば、つなぎ直す。</summary>
    public bool TextureDeviceLost => _session?.TextureDeviceLost ?? false;

    /// <summary>ヘッドセットを外している・スタンバイに入っているか（→実装メモ5.85）。</summary>
    public bool HeadsetStandby => _session?.HeadsetStandby ?? false;

    /// <summary>設定からパネルの見た目を作る。デスクトップのウィンドウも同じ見た目で描く（→実装メモ5.39）。</summary>
    public static PanelStyle CreateStyle(AppSettings settings) => new()
    {
        BackgroundOpacity = settings.BackgroundOpacity,
        IdFontSize = settings.IdFontPixels,
        AuxFontSize = settings.AuxFontPixels,
    };

    public OverlayController Controller => _controller ?? throw new InvalidOperationException("初期化前です。");

    /// <summary>ダッシュボードの設定の画面（→実装メモ5.40）。作れなかったときは null。</summary>
    public SettingsDashboard? Dashboard => _dashboard;

    public bool Initialize(out string error)
    {
        error = string.Empty;

        var baseDirectory = AppContext.BaseDirectory;
        var actionManifest = Path.Combine(baseDirectory, "Resources", "actions.json");
        var appManifest = Path.Combine(baseDirectory, "Resources", "vrcinstancewristory.vrmanifest");

        _session = new SteamVrSession(_log);
        if (!_session.Initialize(out error))
        {
            ConnectError = _session.ConnectError;
            ConnectErrorName = _session.ConnectErrorName;
            return false;
        }

        _session.RegisterApplicationManifest(appManifest);

        // 「SteamVRと一緒に起動する」は SteamVR の登録が正本なので、つないだら読んでおく（→実装メモ5.50）。
        // ダッシュボードの設定の画面はこのあと作るので、先に入れておけば最初からその値で出る。
        _settings.LaunchWithSteamVr = _session.GetAutoLaunch();

        // 入力の初期化は、最初のアクション更新・イベント取得より前に行う。
        _input = new SteamVrInput(_log) { Origin = _session.Origin };
        if (!_input.Initialize(actionManifest))
            _log.Warn("SteamVR Input を初期化できませんでした。パネルは表示しますが、スクロールは使えません。");

        // 振動の調査用（→実装メモ5.79）。設定と、振動のアクションを取れたかを残す。
        _log.Info($"コントローラーの振動: 設定 {(_settings.ControllerVibrationEnabled ? "オン" : "オフ")} / 振動のアクション {(_input.HapticAvailable ? "取得済み" : "取得できない")}");

        try
        {
            _renderer = new PanelRenderer(_style);
            _controller = new OverlayController(_session, _input, _settings, _renderer, _scroll, _log);
            _controller.CreateOverlays();

            // 履歴リセットの予告のアイコン（→実装メモ5.87）。作れなくても手首のパネルは使えるので、つなぐのはやめない。
            try
            {
                var warning = new ResetWarningOverlay(_session, _settings, _log);
                warning.Create();
                _resetWarning = warning;
            }
            catch (OverlayCreateException ex)
            {
                _log.Warn($"履歴リセットの予告のアイコンを作れませんでした: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            // オーバーレイの名前が使用中・数の上限などは、ウィンドウの状態にも出す（→実装メモ5.85）。
            ConnectError = ex is OverlayCreateException create ? create.ConnectError : VrConnectError.OverlayCreate;
            error = $"オーバーレイを準備できません: {ex.Message}";
            _session.Shutdown();
            return false;
        }

        // SteamVRのダッシュボードの設定の画面（→実装メモ5.40）。作れなくても手首のパネルは使える。
        // 手首のパネルとは別の見た目の写しで描く（背景の不透明度を変えても影響しない）。
        var dashboard = new SettingsDashboard(_session, CreateStyle(_settings), DesktopSettings.From(_settings), _log);

        if (dashboard.Create())
            _dashboard = dashboard;
        else
            dashboard.Dispose();

        _lastFrame = _clock.Elapsed;
        return true;
    }

    public void SetContent(IReadOnlyList<Core.Presentation.DisplayRow> rows)
        => Controller.SetContent(rows);

    public void ResetScrollToTail() => _scroll.ResetToTail();

    public void SetCountdown(TimeSpan remaining, TimeSpan total) => Controller.SetCountdown(remaining, total);

    public void SetCountdownStopped(bool stopped) => Controller.CountdownStopped = stopped;

    public void ShowResetWarning() => _resetWarning?.Start(_clock.Elapsed);

    /// <summary>予告通知の設定を変えた。しばらくアイコンを点けたままにする（→実装メモ5.92）。</summary>
    public void PreviewResetWarning() => _resetWarning?.Preview(_clock.Elapsed);

    /// <summary>
    /// 設定の見た目・操作の値を、動いているパネルへ反映する（デスクトップのウィンドウで変えたとき→実装メモ5.39）。
    /// 視線角度の上限とスクロールの速さは <see cref="OverlayController"/> が毎フレーム設定から読むので、ここでは扱わない。
    /// </summary>
    public void ApplySettings()
    {
        if (_controller is null)
            return;

        if (_style.BackgroundOpacity != _settings.BackgroundOpacity)
        {
            _style.BackgroundOpacity = _settings.BackgroundOpacity;
            _controller.InvalidateStyle();
        }

        _controller.SetPanelWidth(_settings.OverlayWidthMeters);
        _controller.SetViewAngleFade(_settings.ViewAngleFadeSeconds);

        // パネルを付ける手首（→実装メモ5.49）。変わったときだけ付け替える。
        _controller.SetWristSide(_settings.Wrist);

        // インスタンス操作の挙動（→実装メモ5.53）。ポップアップの文字と印が変わる。
        _controller.SetReturnAction(_settings.OpenAction);
    }

    /// <summary>
    /// SteamVRと一緒に起動する・しないを切り替え、結果（SteamVRの登録）を返す（→実装メモ5.50）。
    /// つながっていなければ何もせず null。
    /// </summary>
    public bool? SetAutoLaunch(bool enabled) => _session is { Initialized: true } session ? session.SetAutoLaunch(enabled) : null;

    /// <summary>SteamVRのイベントを処理する。終了要求なら false。</summary>
    public bool PollEvents() => _session?.PollEvents() ?? false;

    /// <summary>1フレーム進める。</summary>
    public void Frame(bool contentReady)
    {
        var now = _clock.Elapsed;
        var delta = (float)(now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        // 異常に長い間隔（スリープ復帰など）で一気にスクロールしないよう抑える。
        delta = Math.Clamp(delta, 0f, 0.1f);

        // VRオーバーレイ機能をオフにしている間は、手首のパネルを出さない（→実装メモ5.71）。
        // ダッシュボードの設定の画面は動かし続け、そこから戻せるようにする。
        if (_settings.VrOverlayEnabled)
            _controller?.Update(contentReady, delta);
        else
            _controller?.HideAll();

        // 予告のアイコンは手首のパネルが出ているかによらず出す。VRオーバーレイ機能をオフにしている間だけ出さない。
        _resetWarning?.Update(now, _settings.VrOverlayEnabled && _settings.ResetWarningEnabled);

        _dashboard?.Update(now);
    }

    public void Dispose()
    {
        _controller?.Dispose();
        _resetWarning?.Hide();
        _renderer?.Dispose();

        // ダッシュボードのオーバーレイはセッションの終了でまとめて壊れる。絵の面だけを離す。
        _session?.Dispose();
        _dashboard?.Dispose();
    }
}
