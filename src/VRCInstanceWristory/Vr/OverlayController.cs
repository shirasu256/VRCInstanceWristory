using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Scrolling;
using VRCInstanceWristory.Infrastructure;
using Valve.VR;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// パネルとカーソルの可視性・配置を一元的に決める。
///
/// showPanel  = 内容が揃っている AND パネルを付けた手の追跡が有効 AND 絵を渡せている
///              AND 視線角度の不透明度が 0 より大きい
/// showCursor = showPanel AND 右手の姿勢が有効 AND 見えないレイがUIに命中
///
/// 視線角度の条件は上下左右とも規定内かどうかの二値で、切り替わった時点から
/// viewAngleFadeSeconds をかけて不透明度を 0 ↔ 1 で渡す（2026-09-19のユーザー指定）。
///
/// パネルの高さは行数で変わる（最大7.5行）。オーバーレイは中心を基準に置かれるので、
/// 縮んだぶんだけ中心を下げて、下端を上限の高さのときと同じ位置に保つ。
/// 行が増えたときは上へ伸びる（2026-09-19のユーザー指定）。
///
/// 右手から伸びる線（ビーム）は描かない。レイは命中判定にだけ使う。
///
/// パネルを付ける手首は設定で左右を選べる（2026-09-26のユーザー指定→実装メモ5.49）。
/// 右手首にしたときは右コントローラーへ付け、レイ・スティック・グリップ・トリガーは左手で読む。
/// 以下の「左手」「右手」は、既定（左手首）のときの呼び方である。
///
/// パネルはオーバーレイ1枚で、見出し・行・つまみ・指している行の帯・目印のポップアップは
/// すべて同じテクスチャの中の絵である（→実装メモ5.35）。右手が何を指しているかは、
/// レイとパネルの交点をパネル内のpx座標へ直し、矩形に入っているかで決める。
///
/// 一瞬の追跡欠落・命中の取りこぼしでオーバーレイが点滅しないよう、
/// 追跡と命中には短い猶予を持たせている（2026-09-13のちらつき対策）。
///
/// このファイルには毎フレームの流れ（隠す理由・配置・描き直し）を置き、パネルの上の操作は <c>OverlayController.Interaction.cs</c>、
/// レイとカーソルは <c>OverlayController.Cursor.cs</c>、掴んで置き直すのは <c>OverlayController.Grab.cs</c> に分けてある。
/// </summary>
public sealed partial class OverlayController : IDisposable
{
    private const string PanelKey = "vrcinstancewristory.panel";
    private const string CursorKey = "vrcinstancewristory.cursor";

    /// <summary>
    /// パネルを付けた手の追跡が途切れてもパネルを残す時間。
    /// 右手で左手を隠したときなど、インサイドアウト追跡では短時間の欠落が起きる。
    /// </summary>
    private static readonly TimeSpan TrackingGrace = TimeSpan.FromMilliseconds(500);

    /// <summary>命中が一瞬外れてもカーソルとスクロールを続ける時間。</summary>
    private static readonly TimeSpan HitGrace = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 閉じるボタン（B / Y）を短押しとみなす上限。
    /// VRChatのメインメニューはこれより長く押しても閉じないので、長押しではパネルも消さない。
    /// 2026-09-22に0.2秒から0.15秒、2026-10-01に0.1秒へ縮め、2026-10-02に0.2秒へ戻した（→実装メモ5.25・5.105・5.128）。
    /// 自動検証もこの値で判定を確かめる（検証側で別の値を持たない）。
    /// </summary>
    internal static readonly TimeSpan CloseTapMaxHold = TimeSpan.FromMilliseconds(200);

    /// <summary>絵を渡せなかったことを知らせる間隔。毎フレーム出して埋めない。</summary>
    private static readonly TimeSpan UploadErrorInterval = TimeSpan.FromSeconds(5);

    /// <summary>視線角度の判定に持たせる遊び。境界でのちらつきを防ぐ。</summary>
    private const float ViewAngleHysteresisDegrees = 4f;

    private readonly SteamVrSession _session;
    private readonly SteamVrInput _input;
    private readonly AppSettings _settings;
    private readonly PanelRenderer _renderer;
    private readonly ScrollController _scroll;
    private readonly IDiagnostics _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // パネルはオーバーレイ1枚。見出しも行もつまみもポップアップも同じテクスチャへ描く。
    // カーソルだけは命中点（絶対座標）に置くので、別のオーバーレイのままにしてある。
    private VrOverlay? _panel;
    private VrOverlay? _cursor;

    // いまパネルに描いてある見た目。ここが変わったフレームだけ描き直して渡す。
    private PanelDecorations _decorations;

    /// <summary>新しいバージョンが公開されているか（→実装メモ5.122）。主ループが毎フレーム写す。変われば描き直す。</summary>
    public bool UpdateAvailable { get; set; }
    private float _shownScrollOffset = float.NaN;

    /// <summary>見出し右の残り時間（MM:SS）。</summary>
    private string _countdownText = Countdown.Format(Countdown.Max);

    /// <summary>インスタンス操作の挙動（設定 <c>returnAction</c>→実装メモ5.53）。</summary>
    private Core.Locations.ReturnAction _returnAction;

    /// <summary>パネルを付けている手首（→実装メモ5.49）。</summary>
    private WristSide _side;

    private Vector3 _hitPoint;
    private bool _hasHitPoint;

    /// <summary>行が表示領域に収まらず、スクロールできるか。</summary>
    private bool _scrollable;

    // 直近に決めたパネルの姿勢。指している行を求めるのに毎フレーム使う。
    private HmdMatrix34_t _panelTransform;
    private bool _hasPanelTransform;

    private List<RowLayout> _layouts = [];
    private bool _contentDirty = true;
    private int _contentUpdates;
    private readonly IntervalTimer _uploadErrorInterval = new(UploadErrorInterval);
    private bool _panelHasTexture;
    private uint _transformDevice = OpenVR.k_unTrackedDeviceIndexInvalid;
    private bool _transformDirty = true;

    private Vector3 _translation;
    private Quaternion _rotation;
    private float _panelWidthMeters;

    // パネル操作の振動（→実装メモ5.78）。鳴らすかは設定 controllerVibrationEnabled で決める。
    private readonly PanelHaptics _haptics = new();
    private bool _closePressed;
    private readonly TwoHandTapDetector _closeTap = new(CloseTapMaxHold);
    private bool _viewAngleOk = true;
    private readonly FadeTimer _viewFade;

    private readonly GraceTimer _wristTracking = new(TrackingGrace);
    private readonly GraceTimer _hitHold = new(HitGrace);

    private HmdMatrix34_t _lastWristPose;
    private bool _hasWristPose;

    public OverlayController(
        SteamVrSession session,
        SteamVrInput input,
        AppSettings settings,
        PanelRenderer renderer,
        ScrollController scroll,
        IDiagnostics log)
    {
        _session = session;
        _input = input;
        _settings = settings;
        _renderer = renderer;
        _scroll = scroll;
        _log = log;
        _side = settings.Wrist;
        _returnAction = settings.OpenAction;
        (_translation, _rotation) = settings.PlacementFor(_side);
        _input.OperatingHand = WristSides.Opposite(_side);
        _panelWidthMeters = settings.OverlayWidthMeters;

        // 視線角度が切り替わってから渡しきるまでの時間（設定 viewAngleFadeSeconds）。
        _viewFade = new FadeTimer(TimeSpan.FromSeconds(settings.ViewAngleFadeSeconds));
    }

    private bool PanelVisible => _panel?.Visible ?? false;

    /// <summary>見えないレイがUIに当たっているか（猶予を含む）。</summary>
    private bool Hit { get; set; }

    /// <summary>
    /// 手首の角度で完全に隠れているか（渡しが終わって不透明度が0）。
    ///
    /// インスタンスを移った後、初めてここが true になった時点でパネルを閉じる
    /// （2026-09-21のユーザー指定）。角度を跨いだ瞬間ではなく渡しきってから閉じるので、
    /// 見た目はいつもの薄れ方のまま、戻したときに出てこなくなる。
    /// 追跡が切れているなど角度が分からないときは、隠したことにしない。
    /// </summary>
    public bool ViewAngleHidden => !_viewFade.Visible;

    /// <summary>直近でパネルを隠した理由（ウィンドウの状態の段と <c>--verbose</c> のログに出す）。</summary>
    public PanelHideReason LastHideReason { get; private set; }

    /// <summary>コントローラーに割り当てがないとみなすまでの時間（→実装メモ5.85）。つないだ直後は、割り当てを読むまで働かないことがあるため。</summary>
    private static readonly TimeSpan UnboundDelay = TimeSpan.FromSeconds(10);

    private TimeSpan? _unboundSince;

    /// <summary>操作する手（パネルを付けた手首の反対の手）のコントローラーが見つからないか（→実装メモ5.85）。</summary>
    public bool OperatingHandMissing => _session.ControllerIndex(WristSides.Opposite(_side)) == OpenVR.k_unTrackedDeviceIndexInvalid;

    /// <summary>
    /// 操作する手のコントローラーはあるのに、操作の割り当てが働かないことが続いているか（→実装メモ5.85）。
    /// 同梱の5機種以外で、SteamVR 側にもこのアプリの割り当てがないときにこうなる。
    /// </summary>
    public bool ControllerUnbound => _unboundSince is { } since && _clock.Elapsed - since >= UnboundDelay;

    /// <summary>手首のパネルのテクスチャ（入れ物）を作れないか（→実装メモ5.85）。</summary>
    public bool TextureCreateFailed => _panel is { TextureCreateFailed: true };

    /// <summary>
    /// 絵を渡せないことが何回続いたら「試し直している」と状態に出すか（→実装メモ5.85）。
    /// 1回の失敗で出すと、たまたま1フレームだけ渡せなかったときにも警告が出るので、続いたときだけ出す。
    /// </summary>
    private const int TextureRetryingFailures = 3;

    /// <summary>絵を渡せないことが続いているか（→実装メモ5.85）。直前の絵を残して試し直している。</summary>
    public bool TextureRetrying => _panel is { TextureCreateFailed: false } panel && panel.ConsecutiveFailures >= TextureRetryingFailures;

    /// <summary>
    /// 行の内容が変わって描き直しが必要になった回数（検証用）。
    /// 同じ内容の行を渡し直しても増えない（＝下絵を描き直さない）。
    /// </summary>
    public int ContentUpdates => _contentUpdates;

    /// <summary>パネルを付けたコントローラー基準の現在の相対位置。</summary>
    public Vector3 Translation => _translation;

    /// <summary>パネルを付けたコントローラー基準の現在の相対回転。</summary>
    public Quaternion Rotation => _rotation;

    /// <summary>
    /// 見出しのカウントダウンを更新する。残り時間は呼び出し側が決める
    /// （対象インスタンスに滞在している間は上限の60分で止める）。
    /// 値が変わったフレームだけパネルを描き直す。
    /// </summary>
    public void SetCountdown(TimeSpan remaining, TimeSpan total)
    {
        _countdownText = Countdown.Format(remaining, total);
        _countdownRemaining = remaining;
    }

    /// <summary>見出しの残り時間（文字にする前の値）。残り3分以下の色の行き来に使う（→実装メモ5.130）。</summary>
    private TimeSpan _countdownRemaining = Countdown.Max;

    /// <summary>「延長」で数え直した時刻（<see cref="_clock"/> の経過時間）。残り時間の数字を光らせる（→実装メモ5.130）。</summary>
    private TimeSpan? _countdownFlashedAt;

    /// <summary>残り時間の数字の強調を描き直す間隔を約45回/秒までに抑える（→実装メモ5.133）。</summary>
    private readonly CountdownEmphasisPacer _emphasisPacer = new();

    /// <summary>「延長」で数え直した。残り時間の数字を光らせ、1秒かけて戻す（→実装メモ5.130）。</summary>
    public void FlashCountdown() => _countdownFlashedAt = _clock.Elapsed;

    /// <summary>カウントダウンが止まっているか（→実装メモ5.73）。見出しを「カウントダウン停止中:」にし、「延長」「リセット」を押せなくする。</summary>
    public bool CountdownStopped { get; set; }

    /// <summary>
    /// 「延長」のボタンが押されたら一度だけ true を返す。
    /// 呼び出し側（通常動作なら <c>HistoryEngine.ResetRetention</c>）が実際の数え直しを行う。
    /// </summary>
    public bool TakeResetPressed()
    {
        if (!_resetPressed)
            return false;

        _resetPressed = false;
        return true;
    }

    /// <summary>
    /// 確認で「リセット」が選ばれたら一度だけ true を返す（→実装メモ5.65）。
    /// 呼び出し側（通常動作なら <c>HistoryEngine.ClearHistory</c>）が実際に履歴を消す。
    /// </summary>
    public bool TakeClearPressed()
    {
        if (!_clearPressed)
            return false;

        _clearPressed = false;
        return true;
    }

    /// <summary>
    /// 目印が選ばれていたら一度だけ返す。返すのは「選ばれた印」で、付け外しの判断は呼び出し側が行う
    /// （既に付いている印を選んだときは外す→<see cref="InstanceMarks.Toggle"/>）。
    /// </summary>
    public (string EventId, InstanceMark Mark)? TakeMarkRequest()
    {
        var request = _markRequest;
        _markRequest = null;
        return request;
    }

    /// <summary>
    /// 「ここへ戻る」が選ばれていたら、その行のeventIdを一度だけ返す（→実装メモ5.43）。
    /// 呼び出し側（通常動作なら主ループ）が、その訪問の location から launch URL を開く。
    /// </summary>
    public string? TakeLaunchRequest()
    {
        var request = _launchRequest;
        _launchRequest = null;
        return request;
    }

    /// <summary>パネルを付けている手首（→実装メモ5.49）。</summary>
    public WristSide Wrist => _side;

    /// <summary>
    /// パネルを付ける手首を切り替える（→実装メモ5.49）。その手首の配置を設定から取り直し、
    /// 操作する手も入れ替える。掴んでいる途中なら手放す（保存はしない）。
    /// </summary>
    public void SetWristSide(WristSide side)
    {
        if (side == _side)
            return;

        _side = side;
        _input.OperatingHand = WristSides.Opposite(side);
        (_translation, _rotation) = _settings.PlacementFor(side);

        // 前の手首の姿勢と猶予は持ち越さない。
        _grabbing = false;
        _hasWristPose = false;
        _wristTracking.Reset();
        _hitHold.Reset();
        _transformDevice = OpenVR.k_unTrackedDeviceIndexInvalid;
        _transformDirty = true;

        _log.Info($"パネルを{WristSides.DisplayName(side)}に付け替えました（操作は{(side == WristSide.Right ? "左手" : "右手")}）。");
    }

    /// <summary>インスタンス操作の挙動（→実装メモ5.53）を切り替える。開いているポップアップの文字もその場で変わる。</summary>
    public void SetReturnAction(Core.Locations.ReturnAction action)
    {
        if (action == _returnAction)
            return;

        _returnAction = action;

        if (_popupOpen && IndexOfRow(_popupEventId) is var index and >= 0)
            _popupReturnEnabled = _layouts[index].Row.CanOpen(action);
    }

    /// <summary>
    /// 閉じるボタン（B / Y）が短押しされたら一度だけ true を返す。
    /// 表示中かどうかに関わらず拾い、呼び出し側が表示状態へ反映する。
    /// </summary>
    public bool TakeClosePressed()
    {
        if (!_closePressed)
            return false;

        _closePressed = false;
        return true;
    }

    /// <summary>掴みを離した直後に一度だけ true になる。呼び出し側が設定の保存に使う。</summary>
    public bool TakeGrabReleased()
    {
        if (!_grabReleased)
            return false;

        _grabReleased = false;
        return true;
    }

    /// <summary>配置を差し替える。次のフレームでオーバーレイへ反映する。</summary>
    public void SetPlacement(Vector3 translation, Quaternion rotation)
    {
        _translation = translation;
        _rotation = Quaternion.Normalize(rotation);
        _transformDirty = true;
    }

    /// <summary>パネルの物理幅を変える（設定の「パネルの幅」から）。</summary>
    public void SetPanelWidth(float meters)
    {
        if (meters <= 0f)
            return;

        _panelWidthMeters = meters;
        _panel?.SetWidthInMeters(meters);
        _transformDirty = true;
    }

    /// <summary>視線角度の条件が切り替わってから渡しきるまでの時間を変える（デスクトップのウィンドウから→実装メモ5.39）。</summary>
    public void SetViewAngleFade(float seconds) => _viewFade.Duration = TimeSpan.FromSeconds(Math.Max(0f, seconds));

    /// <summary>
    /// 見た目の設定（背景の不透明度など <see cref="PanelStyle"/> の値）が変わったので、次のフレームで行の下絵から描き直す
    /// （デスクトップのウィンドウから→実装メモ5.39）。
    /// </summary>
    public void InvalidateStyle() => _contentDirty = true;

    public void CreateOverlays()
    {
        _panel = _session.CreateOverlay(PanelKey, "VRC Instance Wristory 訪問履歴");
        _cursor = _session.CreateOverlay(CursorKey, "VRC Instance Wristory カーソル");

        foreach (var overlay in Overlays())
        {
            // マウス入力方式は使わず、レイ判定とアナログ入力をアプリ側で扱う。
            overlay.SetInputMethodNone();
            overlay.SetAlpha(1f);
            overlay.SetVisible(false);
        }

        _panel.SetWidthInMeters(_settings.OverlayWidthMeters);
        _panel.SetSortOrder(10);

        // カーソルはパネルより手前。
        _cursor.SetWidthInMeters(_settings.CursorSizeMeters);
        _cursor.SetSortOrder(16);

        _cursor.SetTexture(CursorTexture(64), 64, 64);
    }

    private IEnumerable<VrOverlay> Overlays()
    {
        if (_panel is not null)
            yield return _panel;

        if (_cursor is not null)
            yield return _cursor;
    }

    /// <summary>
    /// 表示内容を差し替える。行の内容が同じなら、測り直しも描き直しもしない。
    /// 行の下絵はGDI+で1回10msほどかかるので、同じ絵を描き直すのは無駄である（→5.35）。
    /// 滞在中の行の棒は時間とともに伸びるので、行の中身そのものを比べる（→実装メモ5.28）。
    /// </summary>
    public void SetContent(IReadOnlyList<DisplayRow> rows)
    {
        if (SameRows(rows))
            return;

        _layouts = _renderer.Measure(rows);

        // 行が7.5行ぶんに満たないときは、その行のぶんまで縦幅を詰める。
        // 高さが変わるとパネルの中心も動くので、配置を取り直す。
        if (_renderer.SetViewportForContent(_layouts.Sum(l => l.Height)))
            _transformDirty = true;

        _scroll.RowHeight = _renderer.Style.RowHeight;
        _scroll.ViewportHeight = _renderer.ViewportHeight;
        _scroll.SetRows(_renderer.ToScrollRows(_layouts));

        // 行が表示領域に収まらないときだけ、溝とつまみを出す。
        _scrollable = _scroll.ContentHeight > _renderer.ViewportHeight;

        _contentDirty = true;
        _contentUpdates++;
    }

    /// <summary>いま描いてある行と同じ内容か。並び・文言・現在地の印まで含めて比べる。</summary>
    private bool SameRows(IReadOnlyList<DisplayRow> rows)
    {
        if (rows.Count != _layouts.Count)
            return false;

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] != _layouts[i].Row)
                return false;
        }

        return true;
    }

    /// <summary>1フレーム分の更新。</summary>
    /// <param name="contentReady">ログ側の条件（実行中・ワールドタブ・期限内の履歴）が成立しているか。</param>
    public void Update(bool contentReady, float deltaSeconds)
    {
        var now = _clock.Elapsed;
        var frame = _input.Update();

        // 割り当てが働いていないか。スクロールのアクションは両手に割り当ててあるので、働いていれば有効になる。
        // SteamVR のダッシュボードを開いている間は、割り当てがあっても働かないことがあるので数えない。
        var unbound = _input.Ready && !frame.ScrollActive && !OperatingHandMissing && !_session.DashboardVisible;
        _unboundSince = unbound ? _unboundSince ?? now : null;

        // パネルの取り付け先（既定は左手）。右手首にしたときは右コントローラー（→実装メモ5.49）。
        var wristIndex = _session.ControllerIndex(_side);

        // 閉じる操作は、パネルが出ているかどうかに関わらず拾う。
        // VRChatのメインメニューは長押しでは閉じないので、こちらも短押しだけを閉じる合図とする。
        // 片方を押し続けたままもう片方を短押ししても閉じるので、短押しは手ごとに見る（→実装メモ5.96）。
        if (_closeTap.Update(frame.CloseHeldLeft, frame.CloseHeldRight, now))
            _closePressed = true;

        if (wristIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            _wristTracking.Reset();
        }
        else if (_session.TryGetDevicePose(wristIndex, out var wristPose))
        {
            _lastWristPose = wristPose;
            _hasWristPose = true;
            _wristTracking.Signal(now);
        }

        // 追跡が一瞬切れても、猶予のあいだはパネルを消さない。
        var wristTracked = _hasWristPose && _wristTracking.IsHolding(now);

        var hideReason = PanelHideReason.None;

        // 行がなくても、設定「該当履歴が無い場合も表示する」がオンなら「該当する履歴はありません」と書いて出す（→実装メモ5.128）。
        if (!contentReady || (_layouts.Count == 0 && !_settings.ShowPanelWhenEmpty))
            hideReason = PanelHideReason.ContentNotReady;
        else if (wristIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
            hideReason = PanelHideReason.ControllerMissing;
        else if (!wristTracked)
            hideReason = PanelHideReason.WristTrackingLost;

        var panelWorld = _hasWristPose
            ? Math3d.Multiply(_lastWristPose, Math3d.FromTransform(_translation, _rotation))
            : default;

        // 視線角度の条件。掴んでいる間は外す（動かしている最中に消さない）。
        _viewAngleOk = _grabbing
            || !_hasWristPose
            || WithinViewAngle(panelWorld);

        if (hideReason != PanelHideReason.None)
        {
            // 別の理由で隠れている間は渡しを進めない。次に出るときは角度どおりの濃さから始める。
            _viewFade.Snap(_viewAngleOk);
        }
        else
        {
            // 角度が規定を外れた時点から、決まった秒数をかけて薄くする（戻るときも同じ時間で濃くする）。
            _viewFade.Update(_viewAngleOk, deltaSeconds);

            if (!_viewFade.Visible)
                hideReason = PanelHideReason.ViewAngle;
        }

        if (hideReason != PanelHideReason.None)
        {
            // 非表示は描画完了を待たずに行う。
            SetPanelVisible(false, hideReason);
            _cursor?.SetVisible(false);
            Hit = false;
            _hasHitPoint = false;
            ClearResetPointing();
            CloseConfirm();
            ClosePopup();
            ClearHoverRow();
            ReleaseGrab();
            _haptics.Reset();
            _scroll.Update(0f, active: false, deltaSeconds);
            return;
        }

        UpdatePanelTransform(wristIndex);

        // 見た目が変わっていれば描き直して渡す。変わっていなければ何もしない。
        RedrawIfNeeded();

        // 不透明度は絵を渡すこととは別（SetOverlayAlpha なのでちらつかない）。
        ApplyOverlayAlpha(_viewFade.Alpha);

        // 一度も絵を渡せていない間は表示しない。渡せなくても、直前の絵が残っているなら消さない。
        SetPanelVisible(_panelHasTexture, PanelHideReason.TextureNotReady);

        UpdateCursor(frame, now);
        UpdateGrab(frame);

        // トリガーで何かが起きたかを、押す前後の状態の違いで見る（振動→実装メモ5.78）。
        var actionsBefore = _actions;
        var popupWasOpen = _popupOpen;
        var confirmWasOpen = _confirmOpen;

        // 履歴リセットの確認を出している間は、確認だけがトリガーを受け取る（→実装メモ5.65）。
        if (_confirmOpen)
        {
            ClearResetPointing();
            ClosePopup();
            ClearHoverRow();
            UpdateConfirm(frame);
        }
        else
        {
            // トリガーは見出しのボタンと目印の両方で使うので、先に見たほうが取ったら
            // もう一方では使わない（同じ1回の操作で2つ動かさない）。
            var opened = UpdateHeaderButtons(frame, now);
            UpdateMarking(frame, clickTaken: opened || _actions != actionsBefore);
        }

        var pressed = frame.ClickPressed
            && (_actions != actionsBefore
                || _popupOpen != popupWasOpen
                || _confirmOpen != confirmWasOpen);

        if (_haptics.Update(PointedTarget(), pressed, _grabbing) is { } pulse && _settings.ControllerVibrationEnabled)
            Vibrate(pulse, _haptics.LastReason);

        // 掴んでいる間・ポップアップや確認を出している間はスクロールしない。
        var scrollActive = !_grabbing && !_popupOpen && !_confirmOpen && PanelVisible && frame.PointerPoseValid && Hit && frame.ScrollActive;
        _scroll.Deadzone = _settings.ScrollDeadzone;
        _scroll.RowsPerSecond = _settings.ScrollRowsPerSecond;
        _scroll.Update(frame.ScrollY, scrollActive, deltaSeconds);
    }

    /// <summary>
    /// 振動を1回鳴らし、結果を記録する（→実装メモ5.79）。鳴らしたことは <c>--verbose</c> のときだけ、
    /// SteamVR が断ったことは常に（同じ結果が続く間は1回だけ）残す。
    /// </summary>
    private void Vibrate(HapticPulse pulse, string? reason)
    {
        var result = _input.Vibrate(pulse);
        var (duration, frequency, amplitude) = HapticPulses.Shape(pulse);
        _log.Info($"振動: {reason} → {pulse}（{duration * 1000f:0}ms・{frequency:0}Hz・{amplitude:0.00}・{_input.OperatingHand}の手） 結果 {result?.ToString() ?? "アクションなし"}");

        if (result == _lastHapticResult)
            return;

        _lastHapticResult = result;

        if (result is null)
            _log.Warn("振動のアクション（/actions/main/out/haptic）を取得できないため、振動させられません。");
        else if (result != EVRInputError.None)
            _log.Warn($"振動を SteamVR が受け付けませんでした: {result}");
    }

    private EVRInputError? _lastHapticResult = EVRInputError.None;

    /// <summary>
    /// パネル正面と視線の角度。上下・左右のどちらかが規定角度以上なら隠す側へ渡す。
    /// 一度隠れ始めた後は少し戻さないと戻らない（境界で行き来しないようにするため）。
    /// </summary>
    private bool WithinViewAngle(in HmdMatrix34_t panelWorld)
    {
        if (!_session.TryGetDevicePose(OpenVR.k_unTrackedDeviceIndex_Hmd, out var head))
            return true; // 頭の位置が分からないときは条件で隠さない。

        var toHead = Math3d.Translation(head) - Math3d.Translation(panelWorld);

        // パネルのローカル座標系へ移す。+Z が表（法線）。
        var local = new Vector3(
            Vector3.Dot(toHead, Math3d.Right(panelWorld)),
            Vector3.Dot(toHead, Math3d.Up(panelWorld)),
            Vector3.Dot(toHead, Math3d.Back(panelWorld)));

        if (local.Z <= 0.0001f)
            return false; // 裏側からは見せない。

        const float toDegrees = 180f / MathF.PI;
        var horizontal = MathF.Atan2(MathF.Abs(local.X), local.Z) * toDegrees;
        var vertical = MathF.Atan2(MathF.Abs(local.Y), local.Z) * toDegrees;

        var limit = _settings.ViewAngleLimitDegrees;
        if (!_viewAngleOk)
            limit = MathF.Max(1f, limit - ViewAngleHysteresisDegrees);

        return horizontal < limit && vertical < limit;
    }

    private void UpdatePanelTransform(uint wristIndex)
    {
        if (_panel is null)
            return;

        if (_transformDevice == wristIndex && !_transformDirty)
            return;

        var placement = Math3d.FromTransform(_translation, _rotation);

        // 縦幅を詰めたぶんだけ中心を下げ、下端を上限の高さのときと同じ位置に保つ。
        // 掴んで決めた位置（_translation）は上限の高さの中心を指したままなので、設定の意味は変わらない。
        var anchor = PanelGeometry.BottomAnchorOffsetMeters(_renderer.Style, _panelWidthMeters, _renderer.Height);
        var transform = Math3d.Multiply(placement, Math3d.FromTransform(new Vector3(0f, anchor, 0f), Quaternion.Identity));

        // 指している行を求めるのに毎フレーム使うので、いまの姿勢を控えておく
        // （ここを通らないフレームは姿勢が変わっていない＝控えた値がそのまま使える）。
        _panelTransform = transform;
        _hasPanelTransform = true;

        // 失敗したら _transformDirty を残し、次のフレームで試し直す（失敗のログは VrOverlay が間引く）。
        if (!_panel.SetTransformDeviceRelative(wristIndex, transform))
            return;

        if (_transformDevice != wristIndex)
            _log.Info($"パネルを{(_side == WristSide.Right ? "右" : "左")}コントローラー（device {wristIndex}）へ固定しました。");

        _transformDevice = wristIndex;
        _transformDirty = false;
    }

    /// <summary>
    /// 見た目が変わっていれば、パネルを1枚に描き直して渡す。
    ///
    /// 行の文字を描き直すのは行の内容が変わったときだけで、スクロール・指している行・
    /// 残り時間・ポップアップの変化は、下絵から窓を写して上に描き足すだけで済ませる。
    /// 渡すのは <c>SetOverlayTexture</c> なので、毎フレーム渡してもちらつかない（→実装メモ5.34）。
    /// </summary>
    private void RedrawIfNeeded()
    {
        if (_panel is null)
            return;

        var decorations = BuildDecorations();
        var offset = _scroll.Offset;

        var moved = float.IsNaN(_shownScrollOffset) || MathF.Abs(offset - _shownScrollOffset) >= 0.25f;

        if (!_contentDirty && !moved && decorations == _decorations)
            return;

        if (_contentDirty)
        {
            _renderer.RenderRows(_layouts);
            _contentDirty = false;
        }

        _renderer.Compose(offset, decorations);
        Submit();

        _decorations = decorations;
        _shownScrollOffset = offset;
    }

    /// <summary>
    /// 見出しの右側の状態（残り時間と「延長」を出すか→実装メモ5.71・5.73）。
    /// 描くとき（<see cref="BuildDecorations"/>）とボタンの命中を見るとき（<see cref="UpdateHeaderButtons"/>）で同じ値を使う。
    /// </summary>
    private PanelDecorations HeaderState() => new()
    {
        Countdown = _countdownText,
        AutoResetDisabled = !_settings.AutoResetEnabled,
        CountdownStopped = CountdownStopped,
        HistoryEmpty = _layouts.Count == 0,
    };

    /// <summary>いまの状態から、行の上に重ねるものを決める。</summary>
    private PanelDecorations BuildDecorations()
    {
        var style = _renderer.Style;

        var hover = _hasHoverRow
            ? PanelGeometry.RowHighlightRect(style, _renderer.ViewportHeight, _scroll.Offset, _hoverRow.Top, _hoverRow.Height, _scrollable)
            : RectangleF.Empty;

        // 残り時間の数字の強調（→実装メモ5.130）。強さはこのフレームの時刻から求め、描き直しは約45回/秒までに抑える（→実装メモ5.133）。
        // 該当する履歴がない間は消える行がないので、残り3分以下でも行き来させない（→実装メモ5.131）。
        var countingDown = _settings.AutoResetEnabled && !CountdownStopped && _layouts.Count > 0;
        var now = _clock.Elapsed;
        var (glow, warning) = _emphasisPacer.Next(
            now,
            CountdownEmphasis.Glow(now - _countdownFlashedAt),
            CountdownEmphasis.Warning(_countdownRemaining, countingDown));

        return HeaderState() with
        {
            CountdownGlow = glow,
            CountdownWarning = warning,
            ResetPointed = _resetPointed,
            ClearPointed = _clearPointed,
            ConfirmClear = _confirmOpen,
            ConfirmPointed = _confirmPointed,
            HoverRow = hover,
            Popup = _popupOpen ? _popupRect : RectangleF.Empty,
            PopupCurrent = _popupCurrent,
            PopupPointed = _pointedChoice >= 0 && _pointedChoice < InstanceMarks.Choices.Count ? InstanceMarks.Choices[_pointedChoice] : InstanceMark.None,
            PopupReturnAction = _returnAction,
            PopupReturnEnabled = _popupReturnEnabled,
            PopupReturnPointed = _pointedChoice == PanelGeometry.ReturnChoiceIndex,
            UpdateAvailable = UpdateAvailable,
        };
    }

    /// <summary>いま描いてある絵をSteamVRへ渡す。</summary>
    private void Submit()
    {
        if (_panel is null)
            return;

        if (_panel.SetTexture(_renderer.GetPixels(), _renderer.Width, _renderer.Height))
        {
            _panelHasTexture = true;
            return;
        }

        // 渡せなくても直前の絵は残っている。点滅させずに、時々だけ知らせる。
        if (_uploadErrorInterval.TryTick(_clock.Elapsed))
            _log.Error("パネルの絵を渡せませんでした。直前の表示を維持して再試行します。");
    }

    private void SetPanelVisible(bool visible, PanelHideReason reason)
    {
        LastHideReason = visible ? PanelHideReason.None : reason;
        _panel?.SetVisible(visible);
    }

    /// <summary>
    /// パネルとカーソルの不透明度をそろえる。毎フレーム呼んでよい
    /// （見た目が変わらない差は <see cref="VrOverlay.SetAlpha"/> が捨てる）。
    /// </summary>
    private void ApplyOverlayAlpha(float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);

        foreach (var overlay in Overlays())
            overlay.SetAlpha(alpha);
    }

    public void HideAll()
    {
        SetPanelVisible(false, PanelHideReason.ContentNotReady);
        _cursor?.SetVisible(false);
        Hit = false;
        _hasHitPoint = false;
        ClearResetPointing();
        CloseConfirm();
        ClosePopup();
        ClearHoverRow();
    }

    public void Dispose() => HideAll();
}
