using Valve.VR;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// SteamVR へつなげなかった理由のうち、ウィンドウの状態に出すもの（2026-09-28のユーザー指定→実装メモ5.82・5.85）。
/// 段に出す値と詳しい文は <see cref="Desktop.StatusText"/> が決める。
/// </summary>
public enum VrConnectError
{
    /// <summary>出すほどの理由はない（SteamVR の起動の途中でまだ受け付けない、など）。「接続待機中」と出す。</summary>
    None,

    /// <summary>D3D11 デバイスを作れない（E_OUTOFMEMORY＝GPU の資源が尽きている）。</summary>
    TextureDeviceExhausted,

    /// <summary>D3D11 デバイスを作れない（そのほかの理由）。</summary>
    TextureDevice,

    /// <summary>openvr_api.dll がない・読み込めない。</summary>
    OpenVrApiMissing,

    /// <summary>ヘッドセットが見つからない（<c>Init_HmdNotFound</c> など）。</summary>
    HeadsetNotFound,

    /// <summary>SteamVR が古い（<c>Init_InterfaceNotFound</c> など）。</summary>
    RuntimeOutdated,

    /// <summary>SteamVR のインストールが壊れている・見つからない。</summary>
    InstallationBroken,

    /// <summary>SteamVR との通信に失敗する（<c>IPC_*</c>）。</summary>
    Ipc,

    /// <summary>通信に失敗し、SteamVR のサーバーが管理者として動いている（このアプリは違う）。</summary>
    PermissionMismatch,

    /// <summary>ファームウェアの更新・再起動の途中。</summary>
    DeviceBusy,

    /// <summary>そのほかの初期化の失敗。名前は <see cref="SteamVrSession.ConnectErrorName"/>。</summary>
    InitOther,

    /// <summary>IVROverlay を取れない。</summary>
    OverlayUnavailable,

    /// <summary>d3d11.dll・DXGI を読み込めない。</summary>
    Direct3DUnavailable,

    /// <summary>SteamVR が使う GPU を取得できない。</summary>
    AdapterNotFound,

    /// <summary>つながっている途中で GPU がリセットされ、つなぎ直している。</summary>
    GpuReset,

    /// <summary>SteamVR のサーバーは動いているのに、コンポジター（<c>vrcompositor.exe</c>）がいない（→実装メモ5.88）。</summary>
    CompositorDown,

    /// <summary>手首のパネルのオーバーレイを作れない（そのほかの理由）。</summary>
    OverlayCreate,

    /// <summary>オーバーレイの名前が使用中（前の起動が SteamVR に残っている、など）。</summary>
    OverlayKeyInUse,

    /// <summary>SteamVR のオーバーレイの数が上限に達した。</summary>
    OverlayLimit,
}

/// <summary>
/// つながっている間に見つけた、一部が働かない理由（→実装メモ5.85）。いくつも同時に起こりうる。
/// 段に出すのは <see cref="Desktop.StatusText"/> が決めた順で1つだけ。
/// </summary>
[Flags]
public enum VrRuntimeIssue
{
    None = 0,

    /// <summary>1フレームの処理で例外が繰り返し出ている。</summary>
    FrameErrors = 1 << 0,

    /// <summary>手首のパネルのテクスチャを作れない。</summary>
    TextureCreateFailed = 1 << 1,

    /// <summary>絵を SteamVR へ渡せないことが続いている（直前の表示を残して試し直している）。</summary>
    TextureRetrying = 1 << 2,

    /// <summary>操作の割り当て（アクションマニフェスト）を読めない。</summary>
    InputUnavailable = 1 << 3,

    /// <summary>コントローラーはあるのに、操作の割り当てがない（同梱の5機種以外など）。</summary>
    ControllerUnbound = 1 << 4,

    /// <summary>ダッシュボードの設定の画面を作れない・渡せない。</summary>
    DashboardUnavailable = 1 << 5,

    /// <summary>アプリの登録（<c>AddApplicationManifest</c>）に失敗した。</summary>
    ApplicationUnregistered = 1 << 6,
}

/// <summary>オーバーレイを作れなかった（→実装メモ5.85）。理由（<see cref="Error"/>）で状態の表示を分ける。</summary>
public sealed class OverlayCreateException(string key, EVROverlayError error)
    : InvalidOperationException($"オーバーレイ {key} を作成できません: {error}")
{
    public EVROverlayError Error => error;

    /// <summary>ウィンドウの状態に出す理由。</summary>
    public VrConnectError ConnectError => error switch
    {
        EVROverlayError.KeyInUse => VrConnectError.OverlayKeyInUse,
        EVROverlayError.OverlayLimitExceeded => VrConnectError.OverlayLimit,
        _ => VrConnectError.OverlayCreate,
    };
}
