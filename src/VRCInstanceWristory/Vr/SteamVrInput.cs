using System.Runtime.InteropServices;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Infrastructure;
using Valve.VR;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 1回の更新で読み取った入力。
/// ポインター・スクロール・掴み・押すは<b>操作する手</b>（パネルを付けた手首の反対の手）のもの（→実装メモ5.49）。
/// </summary>
public readonly record struct InputFrame(
    bool PointerPoseValid,
    HmdMatrix34_t PointerPose,
    bool ScrollActive,
    float ScrollY,
    bool GrabHeld,
    bool CloseHeldLeft,
    bool CloseHeldRight,
    bool ClickPressed);

/// <summary>
/// SteamVR Input（仕様9.3節）。
/// 機器別の標準bindingとユーザーのbinding変更を前提にし、生のデバイス番号・軸番号を固定しない。
/// SetActionManifestPath は最初の UpdateActionState・イベント取得より前に呼ぶ。
/// </summary>
public sealed class SteamVrInput(IDiagnostics log)
{
    public const string ActionSetName = "/actions/main";
    public const string RightAimAction = "/actions/main/in/right_aim";

    /// <summary>
    /// 左手のaim姿勢（2026-09-26のユーザー指定→実装メモ5.49）。パネルを右手首に付けたとき、左手で指すのに使う。
    /// </summary>
    public const string LeftAimAction = "/actions/main/in/left_aim";

    public const string ScrollAction = "/actions/main/in/scroll";

    /// <summary>パネルを掴むためのボタン（右手グリップ）。</summary>
    public const string GrabAction = "/actions/main/in/grab";

    /// <summary>
    /// パネルを閉じるボタン（B / Y）。左右どちらの手からも受け付ける。
    /// 押した瞬間ではなく押している状態を手ごとに返し、短押しかどうかは <see cref="TwoHandTapDetector"/> が決める。
    /// </summary>
    public const string CloseAction = "/actions/main/in/close_panel";

    /// <summary>パネル上のボタンを押す（操作する手のトリガー）。指している間だけ意味を持つ。</summary>
    public const string ClickAction = "/actions/main/in/click";

    /// <summary>
    /// パネル操作の振動（→実装メモ5.78）。両手に割り当ててあり、鳴らすときに操作する手へ絞る。
    /// </summary>
    public const string HapticAction = "/actions/main/out/haptic";
    public const string RightHandSource = "/user/hand/right";
    public const string LeftHandSource = "/user/hand/left";

    private ulong _actionSet;
    private ulong _rightAim;
    private ulong _leftAim = OpenVR.k_ulInvalidActionHandle;
    private ulong _scroll;
    private ulong _grab;
    private ulong _close;
    private ulong _click;
    private ulong _haptic = OpenVR.k_ulInvalidActionHandle;
    private ulong _rightHand;
    private ulong _leftHand = OpenVR.k_ulInvalidInputValueHandle;
    private VRActiveActionSet_t[] _activeSets = [];

    /// <summary>
    /// 操作する手（→実装メモ5.49）。既定は右手（パネルは左手首）。
    /// スクロール・掴み・押すのアクションは両手に割り当ててあり、読むときにこの手へ絞る。
    /// </summary>
    public WristSide OperatingHand { get; set; } = WristSide.Right;

    /// <summary>操作する手の入力元。</summary>
    private ulong OperatingSource => OperatingHand == WristSide.Left ? _leftHand : _rightHand;

    public bool Ready { get; private set; }

    /// <summary>アクションが使えない理由。使えない場合はスクロールだけを止める。</summary>
    public string? Unavailable { get; private set; }

    public ETrackingUniverseOrigin Origin { get; set; } = ETrackingUniverseOrigin.TrackingUniverseStanding;

    public bool Initialize(string actionManifestPath)
    {
        var full = Path.GetFullPath(actionManifestPath);

        if (!File.Exists(full))
        {
            Unavailable = $"action manifest が見つかりません: {full}";
            log.Error(Unavailable);
            return false;
        }

        var error = OpenVR.Input.SetActionManifestPath(full);
        if (error != EVRInputError.None)
        {
            Unavailable = $"SetActionManifestPath に失敗しました: {error}";
            log.Error(Unavailable);
            return false;
        }

        if (!TryGetActionSet(ActionSetName, ref _actionSet)
            || !TryGetAction(RightAimAction, ref _rightAim)
            || !TryGetAction(ScrollAction, ref _scroll))
        {
            return false;
        }

        // 左手のaim・掴み・閉じる・押すは任意。取得できなくても表示そのものには影響しない。
        if (OpenVR.Input.GetActionHandle(LeftAimAction, ref _leftAim) != EVRInputError.None)
            _leftAim = OpenVR.k_ulInvalidActionHandle;

        if (OpenVR.Input.GetActionHandle(GrabAction, ref _grab) != EVRInputError.None)
            _grab = OpenVR.k_ulInvalidActionHandle;

        if (OpenVR.Input.GetActionHandle(CloseAction, ref _close) != EVRInputError.None)
            _close = OpenVR.k_ulInvalidActionHandle;

        if (OpenVR.Input.GetActionHandle(ClickAction, ref _click) != EVRInputError.None)
            _click = OpenVR.k_ulInvalidActionHandle;

        if (OpenVR.Input.GetActionHandle(HapticAction, ref _haptic) != EVRInputError.None)
            _haptic = OpenVR.k_ulInvalidActionHandle;

        var sourceError = OpenVR.Input.GetInputSourceHandle(RightHandSource, ref _rightHand);
        if (sourceError != EVRInputError.None)
        {
            log.Warn($"入力元 {RightHandSource} を取得できません: {sourceError}");
            _rightHand = OpenVR.k_ulInvalidInputValueHandle;
        }

        var leftError = OpenVR.Input.GetInputSourceHandle(LeftHandSource, ref _leftHand);
        if (leftError != EVRInputError.None)
        {
            log.Warn($"入力元 {LeftHandSource} を取得できません: {leftError}");
            _leftHand = OpenVR.k_ulInvalidInputValueHandle;
        }

        _activeSets =
        [
            new VRActiveActionSet_t
            {
                ulActionSet = _actionSet,
                ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle,
                ulSecondaryActionSet = 0,
                nPriority = 0, // 標準の優先度。シーンアプリの入力を奪わない。
            },
        ];

        Ready = true;
        Unavailable = null;
        log.Info($"SteamVR Input を初期化しました: {full}");
        return true;
    }

    /// <summary>アクション状態を更新してから各値を読む。</summary>
    public InputFrame Update()
    {
        if (!Ready)
            return default;

        var size = (uint)Marshal.SizeOf<VRActiveActionSet_t>();
        var updateError = OpenVR.Input.UpdateActionState(_activeSets, size);

        if (updateError != EVRInputError.None)
        {
            Unavailable = $"UpdateActionState に失敗しました: {updateError}";
            return default;
        }

        // ポインターは操作する手のaim。右手首に付けたときは左手で指す（→実装メモ5.49）。
        var pointerValid = TryGetPose(OperatingHand == WristSide.Left ? _leftAim : _rightAim, out var pointerPose);
        var scrollOk = TryGetAnalog(_scroll, out var y);
        var grabHeld = TryGetDigital(_grab, OperatingSource);

        // B / Y は左右の手に分かれているので、手ごとに読む。入力元を限定せずに読むと左右の OR になり、
        // 片方を押し続けている間のもう片方の短押しが見えなくなる（→実装メモ5.96）。
        var closeHeldLeft = TryGetDigital(_close, _leftHand);
        var closeHeldRight = TryGetDigital(_close, _rightHand);

        // トリガーは両手に割り当ててあるので、操作する手に絞る（パネルを付けた手のトリガーでは押さない）。
        var clickPressed = TryGetDigitalPressed(_click, OperatingSource);

        if (!scrollOk && Unavailable is null)
            Unavailable = "スクロール用のアクションが利用できません。";
        else if (scrollOk)
            Unavailable = null;

        return new InputFrame(pointerValid, pointerPose, scrollOk, y, grabHeld, closeHeldLeft, closeHeldRight, clickPressed);
    }

    /// <summary>
    /// 操作する手のコントローラーを1回振動させる（→実装メモ5.78）。割り当てがない・使えないときは何もしない。
    /// 鳴らすかどうか（設定・どの操作で鳴らすか）は呼び出し側が決める。
    /// 戻り値は SteamVR の結果。アクションがない・初期化前なら null（→実装メモ5.79）。
    /// </summary>
    public EVRInputError? Vibrate(HapticPulse pulse)
    {
        if (!Ready || _haptic == OpenVR.k_ulInvalidActionHandle)
            return null;

        var (duration, frequency, amplitude) = HapticPulses.Shape(pulse);
        return OpenVR.Input.TriggerHapticVibrationAction(_haptic, 0f, duration, frequency, amplitude, OperatingSource);
    }

    /// <summary>振動のアクションを取得できたか（診断用）。</summary>
    public bool HapticAvailable => Ready && _haptic != OpenVR.k_ulInvalidActionHandle;

    /// <summary>
    /// 押された瞬間だけ true（押しっぱなしでは反応しない）。
    /// </summary>
    /// <param name="restrict">読む入力元。両手から受け付けるなら <c>k_ulInvalidInputValueHandle</c>。</param>
    private static bool TryGetDigitalPressed(ulong action, ulong restrict)
    {
        if (action == OpenVR.k_ulInvalidActionHandle)
            return false;

        var data = default(InputDigitalActionData_t);
        var size = (uint)Marshal.SizeOf<InputDigitalActionData_t>();
        var error = OpenVR.Input.GetDigitalActionData(action, ref data, size, restrict);

        return error == EVRInputError.None && data.bActive && data.bState && data.bChanged;
    }

    /// <summary>ボタンが押されているか。割り当てがなければ常に false。</summary>
    /// <param name="restrict">読む入力元。両手から受け付けるなら <c>k_ulInvalidInputValueHandle</c>。</param>
    private static bool TryGetDigital(ulong action, ulong restrict)
    {
        if (action == OpenVR.k_ulInvalidActionHandle)
            return false;

        var data = default(InputDigitalActionData_t);
        var size = (uint)Marshal.SizeOf<InputDigitalActionData_t>();
        var error = OpenVR.Input.GetDigitalActionData(action, ref data, size, restrict);

        return error == EVRInputError.None && data.bActive && data.bState;
    }

    private bool TryGetPose(ulong action, out HmdMatrix34_t pose)
    {
        pose = default;

        if (action == OpenVR.k_ulInvalidActionHandle)
            return false;

        var data = default(InputPoseActionData_t);
        var size = (uint)Marshal.SizeOf<InputPoseActionData_t>();
        var error = OpenVR.Input.GetPoseActionDataRelativeToNow(action, Origin, 0f, ref data, size, OpenVR.k_ulInvalidInputValueHandle);

        if (error != EVRInputError.None || !data.bActive || !data.pose.bPoseIsValid)
            return false;

        pose = data.pose.mDeviceToAbsoluteTracking;
        return true;
    }

    /// <summary>スティックの縦（上が正）。横は使わない。</summary>
    private bool TryGetAnalog(ulong action, out float y)
    {
        y = 0f;

        var data = default(InputAnalogActionData_t);
        var size = (uint)Marshal.SizeOf<InputAnalogActionData_t>();

        // 入力元を操作する手に限定する（スティックは両手に割り当ててある→実装メモ5.49）。
        var restrict = OperatingSource;
        var error = OpenVR.Input.GetAnalogActionData(action, ref data, size, restrict);

        if (error != EVRInputError.None)
            return false;

        if (!data.bActive)
        {
            // 非アクティブな入力は0として扱う。
            return true;
        }

        y = data.y;
        return true;
    }

    private bool TryGetActionSet(string name, ref ulong handle)
    {
        var error = OpenVR.Input.GetActionSetHandle(name, ref handle);
        if (error == EVRInputError.None && handle != OpenVR.k_ulInvalidActionHandle)
            return true;

        Unavailable = $"action set {name} を取得できません: {error}";
        log.Error(Unavailable);
        return false;
    }

    private bool TryGetAction(string name, ref ulong handle)
    {
        var error = OpenVR.Input.GetActionHandle(name, ref handle);
        if (error == EVRInputError.None && handle != OpenVR.k_ulInvalidActionHandle)
            return true;

        Unavailable = $"action {name} を取得できません: {error}";
        log.Error(Unavailable);
        return false;
    }
}
