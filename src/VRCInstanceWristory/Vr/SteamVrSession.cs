using System.Runtime.InteropServices;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr.Direct3D;
using Valve.VR;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// OpenVRの初期化とオーバーレイの管理（仕様9.3節）。
/// VRApplication_Overlay として初期化し、終了時はすべてのオーバーレイを破棄してから終了する。
///
/// 絵は、こちらが持つD3D11のテクスチャを <c>SetOverlayTexture</c> で指し示して渡す。
/// SteamVRは同じ入れ物を見続けるので、書き換えても絵が外れない（→実装メモ5.34）。
/// D3D11を用意できない環境では理由を出して起動しない。
/// </summary>
public sealed partial class SteamVrSession(IDiagnostics log) : IDisposable
{
    public const string ApplicationKey = "vrcinstancewristory.overlay";

    private readonly List<VrOverlay> _overlays = [];
    private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly System.Diagnostics.Stopwatch _poseClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>同じフレーム内で何度も姿勢を取り直さないための保持時間。</summary>
    private readonly IntervalTimer _poseRefresh = new(TimeSpan.FromMilliseconds(4));

    private bool _initialized;
    private OverlayTextureDevice? _textureDevice;

    public bool Initialized => _initialized;

    public ETrackingUniverseOrigin Origin { get; private set; } = ETrackingUniverseOrigin.TrackingUniverseStanding;

    public uint LeftControllerIndex { get; private set; } = OpenVR.k_unTrackedDeviceIndexInvalid;

    /// <summary>右コントローラーの番号。パネルを右手首に付けたときの取り付け先（→実装メモ5.49）。</summary>
    public uint RightControllerIndex { get; private set; } = OpenVR.k_unTrackedDeviceIndexInvalid;

    /// <summary>その手のコントローラーの番号。</summary>
    public uint ControllerIndex(WristSide side) => side == WristSide.Right ? RightControllerIndex : LeftControllerIndex;

    /// <summary>つなげなかった理由のうち、ウィンドウの状態に出すもの（→実装メモ5.82）。</summary>
    public VrConnectError ConnectError { get; private set; }

    /// <summary>
    /// <see cref="ConnectError"/> が「そのほかの初期化の失敗」（<see cref="VrConnectError.InitOther"/>）のとき、その名前（<c>EVRInitError</c>）。
    /// </summary>
    public string? ConnectErrorName { get; private set; }

    public bool Initialize(out string error)
    {
        error = string.Empty;
        ConnectError = VrConnectError.None;
        ConnectErrorName = null;
        var initError = EVRInitError.None;

        try
        {
            OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Overlay);
        }
        catch (Exception ex)
        {
            // openvr_api.dll が見つからない場合など。
            ConnectError = VrConnectError.OpenVrApiMissing;
            error = $"OpenVRを読み込めません: {ex.Message}。openvr_api.dll が実行フォルダーにあるか確認してください。";
            return false;
        }

        if (initError != EVRInitError.None)
        {
            ConnectError = FromInitError(initError);
            ConnectErrorName = ConnectError == VrConnectError.InitOther ? initError.ToString() : null;
            error = $"OpenVRを初期化できません: {initError}。";
            return false;
        }

        _initialized = true;

        if (OpenVR.Overlay is null)
        {
            ConnectError = VrConnectError.OverlayUnavailable;
            error = "IVROverlay を取得できません。";
            Shutdown();
            return false;
        }

        Origin = OpenVR.Compositor?.GetTrackingSpace() ?? ETrackingUniverseOrigin.TrackingUniverseStanding;
        RefreshControllerIndices();

        // 姿勢取得の経路を起動時に一度通しておく。
        // 毎フレームの経路で初めて例外になると、HMDを着けるまで気づけないため。
        RefreshPoses();

        if (!InitializeTextureDevice(out error))
        {
            Shutdown();
            return false;
        }

        log.Info($"OpenVR を初期化しました（tracking origin = {Origin}）。");
        return true;
    }

    /// <summary>
    /// OpenVR の初期化の失敗を、ウィンドウの状態に出す理由へ分ける（→実装メモ5.85）。
    /// 起動の途中でまだ受け付けないだけのもの（<c>Init_Retry</c> など）は <see cref="VrConnectError.None"/> にし、「接続待機中」と出す。
    /// </summary>
    public static VrConnectError FromInitError(EVRInitError error) => error switch
    {
        EVRInitError.None => VrConnectError.None,

        EVRInitError.Init_Retry
            or EVRInitError.Init_NotInitialized
            or EVRInitError.Init_AnotherAppLaunching
            or EVRInitError.Init_ShuttingDown
            or EVRInitError.Init_NoServerForBackgroundApp => VrConnectError.None,

        EVRInitError.Init_HmdNotFound
            or EVRInitError.Init_HmdNotFoundPresenceFailed
            or EVRInitError.Driver_HmdDisplayNotFound
            or EVRInitError.Driver_WirelessHmdNotConnected => VrConnectError.HeadsetNotFound,

        EVRInitError.Init_InterfaceNotFound
            or EVRInitError.Init_InvalidInterface
            or EVRInitError.Init_InstallationTooOld => VrConnectError.RuntimeOutdated,

        EVRInitError.Init_InstallationNotFound
            or EVRInitError.Init_InstallationCorrupt
            or EVRInitError.Init_PathRegistryNotFound
            or EVRInitError.Init_VRClientDLLNotFound
            or EVRInitError.Init_FileNotFound
            or EVRInitError.Init_FactoryNotFound => VrConnectError.InstallationBroken,

        EVRInitError.Init_RebootingBusy
            or EVRInitError.Init_FirmwareUpdateBusy
            or EVRInitError.Init_FirmwareRecoveryBusy
            or EVRInitError.Init_USBServiceBusy => VrConnectError.DeviceBusy,

        _ when error is >= EVRInitError.IPC_ServerInitFailed and <= EVRInitError.IPC_NamespaceUnavailable => VrConnectError.Ipc,

        _ => VrConnectError.InitOther,
    };

    /// <summary>
    /// テクスチャを置くD3D11デバイスを用意する。
    /// アダプターはSteamVRが描画に使うものでなければならない（別のGPUのテクスチャは拒まれる）。
    /// SteamVR が教える GPU の LUID で探し、見つからなければ番号で探す（→実装メモ5.85）。
    ///
    /// 用意できなければ理由を出して起動を止める（<c>SetOverlayRaw</c> は渡すたびにちらつくので、そこへは退避しない→実装メモ5.34）。
    /// SteamVR自体がD3D11で動いているので、ここが通らない環境は実際には想定していない。
    /// </summary>
    private bool InitializeTextureDevice(out string error)
    {
        error = string.Empty;

        var adapterIndex = -1;
        OpenVR.System?.GetDXGIOutputInfo(ref adapterIndex);

        ulong luid = 0;
        OpenVR.System?.GetOutputDevice(ref luid, ETextureType.DirectX, nint.Zero);

        _textureDevice = OverlayTextureDevice.TryCreate(log, unchecked((long)luid), adapterIndex, out var failure);

        if (_textureDevice is not null)
            return true;

        // GPU の資源が尽きているときは、SteamVR もドライバーも正常で、ほかのアプリが使い切っていることが多い（→実装メモ5.82）。
        ConnectError = failure switch
        {
            TextureDeviceFailure.Exhausted => VrConnectError.TextureDeviceExhausted,
            TextureDeviceFailure.Unavailable => VrConnectError.Direct3DUnavailable,
            TextureDeviceFailure.AdapterNotFound => VrConnectError.AdapterNotFound,
            _ => VrConnectError.TextureDevice,
        };

        error = failure switch
        {
            TextureDeviceFailure.Exhausted => "パネルのテクスチャを置くD3D11デバイスを作れません（GPU の資源が足りません）。"
                + "ほかのアプリが GPU ドライバーの資源を使い切っていることがあります。そのアプリを再起動するか、SteamVR・PC を再起動してください。",
            TextureDeviceFailure.Unavailable => "Direct3D 11 を使えません。GPU のドライバーを入れ直してください。",
            TextureDeviceFailure.AdapterNotFound => "SteamVR が使う GPU を取得できません。SteamVR を再起動してください。",
            _ => "パネルのテクスチャを置くD3D11デバイスを作れません。GPU のドライバーを更新するか、SteamVR を再起動してください。",
        };

        return false;
    }

    /// <summary>
    /// 作った D3D11 デバイスが失われたか（GPU のリセット→実装メモ5.85）。失われていれば、つなぎ直すしかない。
    /// </summary>
    public bool TextureDeviceLost => _textureDevice is { } device && device.RemovedReason != 0;

    /// <summary>
    /// ヘッドセットを外している・スタンバイに入っているか（→実装メモ5.85）。
    /// 近接センサーや動きで決まる SteamVR の判定（<c>k_EDeviceActivityLevel_Standby</c>）をそのまま使う。
    /// </summary>
    public bool HeadsetStandby
        => _initialized
           && OpenVR.System is not null
           && OpenVR.System.GetTrackedDeviceActivityLevel(OpenVR.k_unTrackedDeviceIndex_Hmd) == EDeviceActivityLevel.k_EDeviceActivityLevel_Standby;

    /// <summary>SteamVR のダッシュボードが開いているか。開いている間は、アクションが一時的に働かないことがある。</summary>
    public bool DashboardVisible => _initialized && OpenVR.Overlay is not null && OpenVR.Overlay.IsDashboardVisible();

    /// <summary>オーバーレイを1枚作る。</summary>
    public VrOverlay CreateOverlay(string key, string name)
    {
        var handle = OpenVR.k_ulOverlayHandleInvalid;
        var error = OpenVR.Overlay.CreateOverlay(key, name, ref handle);

        if (error != EVROverlayError.None)
            throw new OverlayCreateException(key, error);

        var overlay = new VrOverlay(handle, key, log, TextureDevice);
        _overlays.Add(overlay);
        return overlay;
    }

    /// <summary>
    /// ダッシュボード（コントローラーのシステムボタンで出る画面）に出すオーバーレイを作る（→実装メモ5.40）。
    /// 本体とアイコンの2枚が対になっていて、どちらもSteamVRが出し入れする（こちらからは表示を切り替えない）。
    /// </summary>
    public (VrOverlay Main, VrOverlay Thumbnail) CreateDashboardOverlay(string key, string name)
    {
        var main = OpenVR.k_ulOverlayHandleInvalid;
        var thumbnail = OpenVR.k_ulOverlayHandleInvalid;
        var error = OpenVR.Overlay.CreateDashboardOverlay(key, name, ref main, ref thumbnail);

        if (error != EVROverlayError.None)
            throw new OverlayCreateException(key, error);

        var mainOverlay = new VrOverlay(main, key, log, TextureDevice);
        var thumbnailOverlay = new VrOverlay(thumbnail, key + ".thumbnail", log, TextureDevice);

        _overlays.Add(mainOverlay);
        _overlays.Add(thumbnailOverlay);
        return (mainOverlay, thumbnailOverlay);
    }

    /// <summary>絵を置く D3D11 デバイス。<see cref="Initialize"/> が成功したあとは必ずある。</summary>
    private OverlayTextureDevice TextureDevice => _textureDevice ?? throw new InvalidOperationException("SteamVR へつなぐ前です。");

    /// <summary>SteamVRのイベントを処理する。終了要求を受け取ったら false を返す。</summary>
    public bool PollEvents()
    {
        if (!_initialized || OpenVR.System is null)
            return false;

        var ev = default(VREvent_t);
        var size = (uint)Marshal.SizeOf<VREvent_t>();

        while (OpenVR.System.PollNextEvent(ref ev, size))
        {
            switch ((EVREventType)ev.eventType)
            {
                case EVREventType.VREvent_Quit:
                    log.Info("SteamVRが終了します。オーバーレイを解放します。");
                    OpenVR.System.AcknowledgeQuit_Exiting();
                    return false;

                case EVREventType.VREvent_TrackedDeviceRoleChanged:
                case EVREventType.VREvent_TrackedDeviceActivated:
                case EVREventType.VREvent_TrackedDeviceDeactivated:
                    RefreshControllerIndices();
                    break;
            }
        }

        return true;
    }

    /// <summary>左右の役割は再接続で変わるため、番号を固定しない。</summary>
    public void RefreshControllerIndices()
    {
        if (OpenVR.System is null)
            return;

        var left = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);

        if (left != LeftControllerIndex)
        {
            LeftControllerIndex = left;
            log.Info($"左コントローラーのデバイス番号: {(left == OpenVR.k_unTrackedDeviceIndexInvalid ? "未接続" : left.ToString())}");
        }

        var right = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);

        if (right != RightControllerIndex)
        {
            RightControllerIndex = right;
            log.Info($"右コントローラーのデバイス番号: {(right == OpenVR.k_unTrackedDeviceIndexInvalid ? "未接続" : right.ToString())}");
        }
    }

    public bool TryGetDevicePose(uint deviceIndex, out HmdMatrix34_t pose)
    {
        pose = default;

        if (OpenVR.System is null || deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
            return false;

        RefreshPoses();

        if (deviceIndex >= _poses.Length || !_poses[deviceIndex].bPoseIsValid)
            return false;

        pose = _poses[deviceIndex].mDeviceToAbsoluteTracking;
        return true;
    }

    /// <summary>
    /// 全デバイスの姿勢をまとめて取り直す。1フレームに何度も呼ばれるので、
    /// 短い間隔では前回の結果を使い回す（確保と呼び出しの削減）。
    /// </summary>
    private void RefreshPoses()
    {
        if (!_poseRefresh.TryTick(_poseClock.Elapsed))
            return;

        OpenVR.System.GetDeviceToAbsoluteTrackingPose(Origin, 0f, _poses);
    }

    public void Shutdown()
    {
        if (!_initialized)
            return;

        foreach (var overlay in _overlays)
            overlay.Destroy();

        _overlays.Clear();

        // オーバーレイを壊してからデバイスを離す。逆順にするとSteamVRが解放済みのテクスチャを掴む。
        _textureDevice?.Dispose();
        _textureDevice = null;

        OpenVR.Shutdown();
        _initialized = false;
    }

    public void Dispose() => Shutdown();
}
