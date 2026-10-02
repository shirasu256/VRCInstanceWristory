using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 設定の部品（2026-09-26のユーザー指定→実装メモ5.39・5.40）。
///
/// デスクトップのウィンドウの右側と、SteamVRのダッシュボードの中身は、どちらもこれ1つで描いて操作する。
/// 並べ方だけが違い、ウィンドウは縦1列でタブごとにまとまりを分け（→実装メモ5.42）、ダッシュボードは横長に収まるよう2列にする
/// （<see cref="Layout"/>）。片方にだけ部品を足すと食い違うので、設定を足すときはここへ足す。
///
/// 表示する値は手元の写し（<see cref="Settings"/>）で、変えたら <see cref="DesktopCommand.ChangeSettings"/> を送る。
/// 実際に設定を書き換えるのは主ループで、もう一方の画面で変えた値は <see cref="SetSettings"/> で届く。
///
/// ファイルは役目で分けてある。ここは状態と操作、並べ方は <c>SettingsView.Layout.cs</c>、描き方は <c>SettingsView.Render.cs</c>。
/// 数値の部品の決まりは <see cref="SettingsSteppers"/>、オン・オフの部品の決まりは <see cref="SettingsChecks"/>。
/// </summary>
public sealed partial class SettingsView : IDisposable
{
    /// <summary>
    /// 予告通知の部品の最初の番号（表示位置→実装メモ5.89）。自動検証が部品の番号を数えるのに使う。
    /// </summary>
    public const int WarningStepperFirst = (int)SettingsStepper.WarningPosition;

    /// <summary>「手首パネルのパラメータをデフォルトに戻す」で戻す項目（→実装メモ5.89）。配置と付ける手首は戻さない。</summary>
    public const SettingsField WristParameterFields = SettingsField.OverlayWidth | SettingsField.BackgroundOpacity | SettingsField.ViewAngle
        | SettingsField.ScrollSpeed | SettingsField.EmptyHistory | SettingsField.LoadingScreen | SettingsField.PanelGrab | SettingsField.TriggerMenu | SettingsField.Vibration;

    /// <summary>パネルを付ける手首の選択肢（左から）。</summary>
    private static readonly (WristSide Value, string Label)[] WristChoices =
    [
        (WristSide.Left, WristSides.DisplayName(WristSide.Left)),
        (WristSide.Right, WristSides.DisplayName(WristSide.Right)),
    ];

    /// <summary>インスタンス操作の挙動の選択肢（上から→実装メモ5.53・5.65）。</summary>
    private static readonly (ReturnAction Value, string Label)[] ReturnChoices =
    [
        (ReturnAction.Browser, "ブラウザでインスタンスリンクを開く"),
        (ReturnAction.VrChat, "VRChat クライアントを再起動して join"),
    ];

    /// <summary>写真を開くアプリの選択肢（左から→実装メモ5.55・5.58）。</summary>
    private static readonly PhotoViewerKind[] ViewerChoices = [PhotoViewerKind.Default, PhotoViewerKind.Custom];

    private readonly PanelStyle _style;
    private readonly Action<DesktopCommand> _emit;

    private UiPainter? _painter;
    private UiPainter? _emphasisPainter;
    private DesktopSettings _settings;

    // 押された部品が、ウィンドウ（Win32）にしか出せない画面を頼んできたもの。ウィンドウが Take… で取り出す。
    private bool _pendingAppChoice;
    private GroupFileRequest _pendingGroupFile;
    private bool _pendingUpdate;

    // 「コピー」を押してから、ポインターがボタンを離れるまで「コピー済み」と出す。
    private bool _copied;

    // ポインター（マウス、またはダッシュボードのレーザー）。
    private PointF? _pointer;
    private HitTarget? _pointedTarget;

    // 押せない部品も含めて、ポインターの下にある部品。押せない理由の吹き出しに使う（→実装メモ5.67）。
    private HitTarget? _hoveredTarget;
    private HitTarget? _pressed;

    public SettingsView(PanelStyle style, DesktopSettings settings, Action<DesktopCommand> emit)
    {
        _style = style;
        _settings = settings;
        _emit = emit;
    }

    /// <summary>設定の部品の種類。</summary>
    public enum HitKind
    {
        StepperMinus,
        StepperPlus,
        TargetType,
        TopMost,
        ResetPlacement,

        /// <summary>パネルを付ける手首（番号は 0 = 左手首、1 = 右手首→実装メモ5.49）。</summary>
        WristSide,

        /// <summary>インスタンス操作の挙動（番号は 0 = ブラウザ、1 = VRChat→実装メモ5.53）。</summary>
        ReturnAction,

        /// <summary>写真を開くアプリ（番号は 0 = 既定のアプリ、1 = 選んだアプリ→実装メモ5.55・5.58）。</summary>
        PhotoViewer,

        /// <summary>名前を付けたグループも Group ID を並べるか（→実装メモ5.48）。</summary>
        GroupIdWithName,

        /// <summary>グループ名をファイルから読み込む（→実装メモ5.65）。<see cref="FileDialogs"/> のときだけ出す。</summary>
        GroupImport,

        /// <summary>グループ名をファイルへ書き出す（→実装メモ5.65）。<see cref="FileDialogs"/> のときだけ出す。</summary>
        GroupExport,

        /// <summary>SteamVRと一緒に起動する（→実装メモ5.50）。SteamVRにつながっていない間は押せない。</summary>
        LaunchWithSteamVr,

        /// <summary>Windowsのログオン時に起動する（→実装メモ5.51）。</summary>
        LaunchAtLogon,

        /// <summary>ロード画面の間も手首のパネルを出すか（→実装メモ5.62）。</summary>
        ShowDuringLoading,

        /// <summary>該当する履歴がないときも手首のパネルを出すか（→実装メモ5.128）。</summary>
        ShowWhenEmpty,

        /// <summary>VRオーバーレイ機能を有効にする（→実装メモ5.71）。</summary>
        VrOverlay,

        /// <summary>履歴の自動リセットを有効にする（→実装メモ5.71）。</summary>
        AutoReset,

        /// <summary>コントローラーの振動を有効にする（→実装メモ5.78）。</summary>
        Vibration,

        /// <summary>手首パネルの移動（掴んで動かす）を有効にする（→実装メモ5.78）。</summary>
        PanelGrab,

        /// <summary>外部からの履歴リセットコマンドを受け付ける（→実装メモ5.83）。</summary>
        ExternalReset,

        /// <summary>外部連携のコマンドをクリップボードへ写す（→実装メモ5.83）。</summary>
        CopyCommand,

        /// <summary>行を指してトリガーを引くと操作メニューを出す（→実装メモ5.86）。</summary>
        TriggerMenu,

        /// <summary>直前の履歴のリセットを戻す（→実装メモ5.86）。戻せるリセットがない間は押せない。</summary>
        UndoClear,

        /// <summary>リセット予告アイコンを表示する（→実装メモ5.89）。</summary>
        ResetWarning,

        /// <summary>AFKから復帰時に予告を再表示する（→実装メモ5.89）。</summary>
        WarningReshow,

        /// <summary>AFK中はカウントダウンを停止する（→実装メモ5.89）。</summary>
        AfkPause,

        /// <summary>手首パネルのパラメータ（配置と付ける手首以外）をデフォルトに戻す（→実装メモ5.89）。</summary>
        ResetWristParameters,

        /// <summary>滞在中はカウントダウンを停止する（→実装メモ5.90。名前は5.91で短くした）。</summary>
        TargetPause,

        /// <summary>VRChatのAFKを検知する（→実装メモ5.98）。</summary>
        AfkDetection,

        /// <summary>新しい版を自動で確かめる（→実装メモ5.121）。</summary>
        UpdateCheck,

        /// <summary>新しい版を今すぐ確かめる（→実装メモ5.121）。インストーラーで入れた版でなければ押せない。</summary>
        CheckUpdates,

        /// <summary>更新して再起動（→実装メモ5.121）。新しい版があるときだけ押せる。確かめる画面はウィンドウ（Win32）が出す。</summary>
        ApplyUpdate,
    }

    /// <summary>押せる部品1つ。数値の部品の <see cref="Index"/> は <see cref="SettingsStepper"/> の番号、選択肢は左（上）からの番号。</summary>
    private readonly record struct HitTarget(HitKind Kind, RectangleF Rect, int Index = 0)
    {
        public SettingsStepper Stepper => (SettingsStepper)Index;
    }

    public DesktopSettings Settings => _settings;

    /// <summary>描き直しが必要か。<see cref="Render"/> で下ろす。</summary>
    public bool Dirty { get; private set; } = true;

    /// <summary>−／＋ を押し続けている（呼び出し側が押し続けの繰り返しを進める）。</summary>
    public bool Repeating => _pressed is { Kind: HitKind.StepperMinus or HitKind.StepperPlus };

    /// <summary>並べた範囲（<see cref="Layout"/> の結果）。</summary>
    public RectangleF Bounds { get; private set; }

    /// <summary>
    /// ファイルを選ぶ画面を出せる置き場所か（デスクトップのウィンドウ）。立っていれば、グループ名の読み込み・書き出しのボタンを出す（→実装メモ5.65）。
    /// ダッシュボードはVRの中なので、デスクトップに出るファイルの画面は使えない。
    /// </summary>
    public bool FileDialogs { get; init; }

    /// <summary>
    /// 「リセットまでの時間」と「訪問履歴に含めるインスタンスタイプ」の選択肢を、列の幅で取れる最大の大きさで描くか（ほかの部品は1倍のまま）。
    /// 倍率は「リセットまでの時間」の名前・−／＋・値が、名前と − の間を名前の字1つぶん空けて1行に収まる最大にする（<see cref="FitEmphasisScale"/>）。
    /// ダッシュボードは左の列が右より短く下が空くので、この2つを大きくして埋める（2026-10-01のユーザー指定→実装メモ5.106）。
    /// </summary>
    public bool EmphasizeToFit { get; init; }

    /// <summary>いまの並びで「リセットまでの時間」とインスタンスタイプの選択肢に掛けている倍率（<see cref="EmphasizeToFit"/> でなければ1）。</summary>
    public float EmphasisScale { get; private set; } = 1f;

    private UiPainter Painter => _painter ?? throw new InvalidOperationException("Layout の前です。");

    /// <summary><see cref="EmphasisScale"/> を掛けた描き手。倍率が1なら <see cref="Painter"/> と同じ。</summary>
    private UiPainter Emphasis => _emphasisPainter ?? Painter;

    private float S(float logical) => Painter.S(logical);

    /// <summary><see cref="EmphasisScale"/> を掛けた画素。</summary>
    private float E(float logical) => Emphasis.S(logical);

    /// <summary>
    /// 写真を開くアプリを選んでほしい、という依頼（→実装メモ5.55）を取り出す。「選んだアプリ」を押すと立つ。
    /// ファイルを選ぶ画面はウィンドウ（Win32）が出し、選んだら <see cref="ChoosePhotoViewer"/> で返す。
    /// </summary>
    public bool TakeAppChoiceRequest()
    {
        var pending = _pendingAppChoice;
        _pendingAppChoice = false;
        return pending;
    }

    /// <summary>グループ名の読み込み・書き出しのボタンを押した、という依頼を取り出す。ファイルを選ぶ画面はウィンドウ（Win32）が出す。</summary>
    public GroupFileRequest TakeGroupFileRequest()
    {
        var pending = _pendingGroupFile;
        _pendingGroupFile = GroupFileRequest.None;
        return pending;
    }

    /// <summary>
    /// 「更新して再起動」を押した、という依頼を取り出す（→実装メモ5.121）。
    /// 確かめる画面はウィンドウ（Win32）が出し、「はい」なら <see cref="DesktopCommand.ApplyUpdate"/> を送る。
    /// </summary>
    public bool TakeUpdateRequest()
    {
        var pending = _pendingUpdate;
        _pendingUpdate = false;
        return pending;
    }

    /// <summary>写真を開くアプリの実行ファイルを選んだ（ウィンドウのファイルを選ぶ画面から）。</summary>
    public void ChoosePhotoViewer(string executablePath)
        => ChangeSettings(_settings with { PhotoViewer = PhotoViewerKind.Custom, PhotoViewerPath = executablePath }, SettingsField.PhotoViewer);

    /// <summary>
    /// もう一方の画面（ウィンドウかダッシュボード）で変わった値を受け取る。送り返しはしない。
    /// </summary>
    public void SetSettings(DesktopSettings settings)
    {
        if (settings == _settings)
            return;

        var relayout = ChangesNotes(settings);
        _settings = settings;
        Dirty = true;

        if (relayout)
            Relayout();
    }

    /// <summary>
    /// SteamVR・Windows の登録の状態だけを差し替える（→実装メモ5.50・5.51）。
    /// 登録を変えた結果は変えた側にも返すので、ほかの値（押している途中の −／＋ など）は触らない。
    /// </summary>
    public void SetStartupState(bool? launchWithSteamVr, bool launchAtLogon)
        => SetSettings(_settings with { LaunchWithSteamVr = launchWithSteamVr, LaunchAtLogon = launchAtLogon });

    /// <summary>注記の中身が変わる変更か（写真を開くアプリの名前を注記に出している→<see cref="PhotoViewerNote"/>）。</summary>
    private bool ChangesNotes(DesktopSettings next)
        => next.PhotoViewer != _settings.PhotoViewer || !string.Equals(next.PhotoViewerPath, _settings.PhotoViewerPath, StringComparison.OrdinalIgnoreCase);

    private void ChangeSettings(DesktopSettings next, SettingsField fields)
    {
        var relayout = ChangesNotes(next);
        _settings = next;
        _emit(new DesktopCommand.ChangeSettings(next, fields));
        Dirty = true;

        if (relayout)
            Relayout();
    }

    // ------------------------------------------------------------------ 値を変える

    private void Step(SettingsStepper id, int direction)
    {
        if (!StepperEnabled(id))
            return;

        var spec = SettingsSteppers.Spec(id);
        var current = spec.Get(_settings);
        var next = spec.NextValue(current, direction);

        if (next == current)
            return;

        ChangeSettings(spec.With(_settings, next), spec.Field);
    }

    private void ToggleTargetType(int index)
    {
        var type = TargetAccessTypes.Selectable[index];
        var types = new HashSet<AccessType>(_settings.TargetTypes);

        if (!types.Remove(type))
            types.Add(type);

        // 1つも記録しない設定にはしない（何も出なくなるだけで、意図した操作とは考えにくい）。
        if (types.Count == 0)
            return;

        ChangeSettings(_settings with { TargetTypes = types }, SettingsField.TargetAccessTypes);
    }

    private void Toggle(CheckSpec check)
    {
        var on = !check.Get(_settings);
        var next = check.With(_settings, on);

        switch (check.Kind)
        {
            // AFK を使う2つの設定は、オンにしたときに「VRChatのAFKを検知する」も一緒にオンにする（2026-09-30のユーザー指定→実装メモ5.99）。
            // 主ループがその場で OSC の受け口を開くので、Windows のファイアウォールの許可の画面もこのクリックで出る。
            // 2つともオフにしたら検知もオフにして、受け口を閉じる。
            case HitKind.WarningReshow:
                ChangeAfkUse(next, on || AfkPauseActive(_settings), check.Field);
                break;

            case HitKind.AfkPause:
                ChangeAfkUse(next, on || (_settings.ResetWarningEnabled && ReshowActive(_settings)), check.Field);
                break;

            // 初期設定の画面の「VRChatのAFKを検知する」をオンにしたら、AFK を使う2つの設定も一緒にオンにする（2026-10-01のユーザー指定→実装メモ5.116）。
            // ファイアウォールでブロックされていてオフに戻すときは、主ループが2つの値もオンにする前へ戻す（→5.111）。
            case HitKind.AfkDetection when on:
                ChangeSettings(next with { PauseCountdownWhileAfk = true, WarningReshowAfterAfk = true },
                    check.Field | SettingsField.AfkPause | SettingsField.ResetWarning);
                break;

            default:
                ChangeSettings(next, check.Field);
                break;
        }
    }

    /// <summary>AFK を使う設定を変え、検知の切り替えが変わるなら一緒に送る。</summary>
    private void ChangeAfkUse(DesktopSettings next, bool detect, SettingsField field)
    {
        if (detect == next.AfkDetectionEnabled)
        {
            ChangeSettings(next, field);
            return;
        }

        ChangeSettings(next with { AfkDetectionEnabled = detect }, field | SettingsField.AfkDetection);
    }

    /// <summary>
    /// 「AFK中はカウントダウンを停止する」が働いているか。値がオンでも、AFK を検知していなければ働かないので、オフに見せる（→実装メモ5.99）。
    /// </summary>
    public static bool AfkPauseActive(DesktopSettings settings) => settings.PauseCountdownWhileAfk && settings.AfkDetectionEnabled;

    /// <summary>「AFKから復帰時に再表示する」が働いているか（同上）。既定の値はオンだが、検知をオンにするまではオフに見せる。</summary>
    public static bool ReshowActive(DesktopSettings settings) => settings.WarningReshowAfterAfk && settings.AfkDetectionEnabled;

    /// <summary>
    /// 手首パネルのパラメータを既定値にした写し（パネルの幅・背景の不透明度・角度・フェード・スクロールの速さ・4つの切り替え）。
    /// コントローラーの振動は手首パネルのまとまりへ移したので、一緒に戻す（→実装メモ5.90）。
    /// </summary>
    private static DesktopSettings WithWristDefaults(DesktopSettings settings)
    {
        var defaults = DesktopSettings.From(new AppSettings());

        return settings with
        {
            PanelWidthMeters = defaults.PanelWidthMeters,
            BackgroundOpacity = defaults.BackgroundOpacity,
            ViewAngleLimitDegrees = defaults.ViewAngleLimitDegrees,
            ViewAngleFadeSeconds = defaults.ViewAngleFadeSeconds,
            ScrollRowsPerSecond = defaults.ScrollRowsPerSecond,
            ShowWhenEmpty = defaults.ShowWhenEmpty,
            ShowDuringLoading = defaults.ShowDuringLoading,
            PanelGrabEnabled = defaults.PanelGrabEnabled,
            TriggerMenuEnabled = defaults.TriggerMenuEnabled,
            VibrationEnabled = defaults.VibrationEnabled,
        };
    }

    // ------------------------------------------------------------------ 押せるか

    private bool IsEnabled(HitTarget target) => target.Kind switch
    {
        // SteamVRにつながっていない間は、SteamVR側の登録が分からないので切り替えさせない（→実装メモ5.50）。
        HitKind.LaunchWithSteamVr => _settings.LaunchWithSteamVr is not null,

        // 戻せるリセットがあるときだけ押せる（→実装メモ5.86）。自動リセットを無効にしていても、手でのリセットは戻せる。
        HitKind.UndoClear => _settings.UndoResetAvailable,

        // インストーラーで入れた版でなければ、確かめられない。確かめている・落としている最中は押させない（→実装メモ5.121）。
        HitKind.UpdateCheck => UpdateOf(_settings).Phase != UpdatePhase.Unavailable,
        HitKind.CheckUpdates => UpdateOf(_settings).Phase is not (UpdatePhase.Unavailable or UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Ready or UpdatePhase.Waiting),
        HitKind.ApplyUpdate => UpdateOf(_settings).Phase == UpdatePhase.Available,

        HitKind.StepperMinus or HitKind.StepperPlus => StepperEnabled(target.Stepper),

        // 予告通知は VR オーバーレイの機能なので、VR オーバーレイ機能をオフにしている間は止める。
        // 「リセット予告アイコンを表示する」をオフにしている間は、その下の部品を止める（→実装メモ5.89）。
        HitKind.WarningReshow => _settings.VrOverlayEnabled && _settings.ResetWarningEnabled,
        HitKind.ResetWarning or HitKind.ResetWristParameters => _settings.VrOverlayEnabled,

        // VRオーバーレイ機能をオフにしている間は、手首パネルの設定をすべて止める（→実装メモ5.71）。
        // コントローラーの振動も手首パネルの操作で鳴らすものなので、同じく止める（→実装メモ5.78）。
        HitKind.WristSide or HitKind.ShowWhenEmpty or HitKind.ShowDuringLoading or HitKind.ResetPlacement
            or HitKind.PanelGrab or HitKind.Vibration or HitKind.TriggerMenu => _settings.VrOverlayEnabled,
        _ => true,
    };

    /// <summary>数値の部品を動かせるか（ホイールも同じ決まり）。</summary>
    private bool StepperEnabled(SettingsStepper id) => SettingsSteppers.Spec(id).Group switch
    {
        // 自動リセットを無効にしている間は、リセットまでの時間を変えられない（→実装メモ5.71）。
        StepperGroup.History => _settings.AutoResetEnabled,
        StepperGroup.ResetWarning => _settings.VrOverlayEnabled && _settings.ResetWarningEnabled,
        _ => _settings.VrOverlayEnabled,
    };

    // ------------------------------------------------------------------ ポインター

    /// <summary>ポインターが動いた（null なら外れた）。見た目が変わったら <see cref="Dirty"/> が立つ。</summary>
    public void PointerMove(PointF? point)
    {
        _pointer = point;
        var pointed = point is { } p ? HitAt(p) : null;
        var hovered = point is { } q ? TargetAt(q) : null;

        if (pointed == _pointedTarget && hovered == _hoveredTarget)
            return;

        // 「コピー済み」は、ポインターが「コピー」を離れたら元へ戻す。
        if (_copied && pointed is not { Kind: HitKind.CopyCommand })
            _copied = false;

        _pointedTarget = pointed;
        _hoveredTarget = hovered;
        Dirty = true;
    }

    /// <summary>押した。部品の上なら true（その部品を働かせる）。</summary>
    public bool PointerDown(PointF point)
    {
        PointerMove(point);

        if (HitAt(point) is not { } target)
            return false;

        _pressed = target;
        Activate(target);
        return true;
    }

    /// <summary>押し続けている間の繰り返し（−／＋）。指したまま外れていなければもう一度働かせる。</summary>
    public void RepeatPress()
    {
        if (_pressed is { } pressed && Repeating && _pointer is { } pointer && pressed.Rect.Contains(pointer))
            Activate(pressed);
    }

    public void PointerUp()
    {
        if (_pressed is null)
            return;

        _pressed = null;
        Dirty = true;
    }

    /// <summary>ホイール。数値の部品の行の上なら値を動かして true。</summary>
    public bool Wheel(PointF point, int delta)
    {
        foreach (var (id, row) in _stepperRows)
        {
            if (!row.Contains(point))
                continue;

            // 使えない行の上では、値を動かさずに受け取る（ウィンドウが設定を送らないように）。
            Step(id, Math.Sign(delta));
            return true;
        }

        return false;
    }

    /// <summary>その位置が押せる部品の上か。</summary>
    public bool IsClickable(PointF point) => HitAt(point) is not null;

    private void Activate(HitTarget target)
    {
        if (SettingsChecks.TryGet(target.Kind, out var check))
        {
            Toggle(check);
            Dirty = true;
            return;
        }

        switch (target.Kind)
        {
            case HitKind.StepperMinus:
                Step(target.Stepper, -1);
                break;

            case HitKind.StepperPlus:
                Step(target.Stepper, +1);
                break;

            case HitKind.TargetType:
                ToggleTargetType(target.Index);
                break;

            case HitKind.ResetPlacement:
                _emit(new DesktopCommand.ResetPlacement());
                break;

            case HitKind.WristSide when WristChoices[target.Index].Value != _settings.Wrist:
                ChangeSettings(_settings with { Wrist = WristChoices[target.Index].Value }, SettingsField.WristSide);
                break;

            case HitKind.ReturnAction when ReturnChoices[target.Index].Value != _settings.ReturnAction:
                ChangeSettings(_settings with { ReturnAction = ReturnChoices[target.Index].Value }, SettingsField.ReturnAction);
                break;

            // 「選んだアプリ」は、押すたびにファイルを選ぶ画面を出す（選び直せるように）。選ばずに閉じたら何も変えない。
            case HitKind.PhotoViewer when ViewerChoices[target.Index] == PhotoViewerKind.Custom:
                _pendingAppChoice = true;
                break;

            case HitKind.PhotoViewer when ViewerChoices[target.Index] != _settings.PhotoViewer:
                ChangeSettings(_settings with { PhotoViewer = ViewerChoices[target.Index] }, SettingsField.PhotoViewer);
                break;

            case HitKind.GroupImport:
                _pendingGroupFile = GroupFileRequest.Import;
                break;

            case HitKind.GroupExport:
                _pendingGroupFile = GroupFileRequest.Export;
                break;

            case HitKind.CheckUpdates:
                _emit(new DesktopCommand.CheckForUpdates());
                break;

            case HitKind.ApplyUpdate:
                _pendingUpdate = true;
                break;

            case HitKind.ResetWristParameters:
                ChangeSettings(WithWristDefaults(_settings), WristParameterFields);
                break;

            case HitKind.UndoClear:
                // 押せるかは主ループが知らせ直す（戻したら押せなくなる）。
                _emit(new DesktopCommand.UndoClearHistory());
                break;

            case HitKind.CopyCommand:
                // クリップボードへ写すのは主ループ（ダッシュボードから押したときも同じ経路にする）。
                _emit(new DesktopCommand.CopyText(ExternalCommandListener.CommandLine()));
                _copied = true;
                break;
        }

        Dirty = true;
    }

    /// <summary>押せるかによらず、その位置の部品。</summary>
    private HitTarget? TargetAt(PointF point)
    {
        foreach (var target in _targets)
        {
            if (target.Rect.Contains(point))
                return target;
        }

        return null;
    }

    private HitTarget? HitAt(PointF point)
    {
        foreach (var target in _targets)
        {
            if (target.Rect.Contains(point) && IsEnabled(target))
                return target;
        }

        return null;
    }

    private bool IsPointed(HitTarget target) => _pointedTarget == target;

    /// <summary>押せない部品を指したときに出す理由（→実装メモ5.67）。押せる部品・理由のない部品は null。</summary>
    private string? DisabledHint(HitTarget target) => target.Kind switch
    {
        HitKind.LaunchWithSteamVr when !IsEnabled(target) => "SteamVR を起動中のみ変更できます",
        HitKind.UndoClear when !IsEnabled(target) => "戻せるリセットはありません",
        HitKind.UpdateCheck or HitKind.CheckUpdates or HitKind.ApplyUpdate when UpdateOf(_settings).Phase == UpdatePhase.Unavailable
            => "自動アップデートを利用するにはインストーラー版をインストールしてください",
        HitKind.ApplyUpdate when !IsEnabled(target) => "新バージョンはありません",
        _ => null,
    };

    /// <summary>いま出している吹き出しの文字。出していなければ null。</summary>
    public string? HintText => _hoveredTarget is { } hovered ? DisabledHint(hovered) : null;

    /// <summary>アップデートの状態（主ループから届いていなければ「使えない」とみなす）。</summary>
    private static UpdateStatus UpdateOf(DesktopSettings settings) => settings.Update ?? UpdateStatus.Unavailable;

    /// <summary>アップデートのまとまりの1行目（→実装メモ5.121）。</summary>
    public static string UpdateVersionText => $"現在のバージョン: {AppInfo.DisplayVersion}";

    /// <summary>
    /// アップデートのまとまりの2行目（状態）。1行目と並べると入り切らないので、行を分けた（→実装メモ5.122）。
    /// 時刻はこのPCの時刻で出す。
    /// </summary>
    public static string UpdateStatusText(UpdateStatus status)
    {
        var checkedAt = status.CheckedAtUtc is { } utc
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" ({DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime():MM/dd HH:mm})")
            : string.Empty;

        return status.Phase switch
        {
            UpdatePhase.Unavailable => "自動アップデートは利用できません",
            UpdatePhase.Idle => string.Empty,
            UpdatePhase.Checking => "確認しています…",
            UpdatePhase.UpToDate => "最新のバージョンです",
            UpdatePhase.Available => $"新バージョン v{status.Version} が公開されています",
            UpdatePhase.Downloading => $"v{status.Version} をダウンロードしています… {status.Progress}%",
            UpdatePhase.Ready => $"v{status.Version} に更新して起動し直します",
            UpdatePhase.Waiting => "ログの読み込み完了を待っています…",
            _ => $"バージョン確認に失敗しました{checkedAt}",
        };
    }

    /// <summary>「最終実行」の段の文字（→実装メモ5.83）。時刻はこのPCの時刻で出す。</summary>
    public static string LastRunText(DateTime? lastRunUtc)
        => lastRunUtc is { } utc
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"最終実行: {DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime():yyyy-MM-dd HH:mm}")
            : "コマンド実行履歴無し";

    // ------------------------------------------------------------------ 自動検証とダッシュボードの見本から見るもの

    /// <summary>設定の値を表示に使う書式。<paramref name="index"/> は <see cref="SettingsStepper"/> の番号。</summary>
    public string StepperText(int index)
    {
        var spec = SettingsSteppers.All[index];
        return spec.Format(spec.Get(_settings));
    }

    /// <summary>設定の部品の矩形。その部品を出していなければ null。</summary>
    public RectangleF? TargetRect(HitKind kind, int index = 0)
        => _targets.FirstOrDefault(t => t.Kind == kind && t.Index == index) is { Rect.Width: > 0 } t ? t.Rect : null;

    public void Dispose()
    {
        _painter?.Dispose();
        _emphasisPainter?.Dispose();
    }
}
