namespace VRCInstanceWristory.Vr.Direct3D;

/// <summary>D3D11 デバイスを作れなかった理由（→実装メモ5.82・5.85）。</summary>
public enum TextureDeviceFailure
{
    None,

    /// <summary>d3d11.dll・DXGI を読み込めない・DXGI の factory を作れない。</summary>
    Unavailable,

    /// <summary>SteamVR が使う GPU を取得できない。</summary>
    AdapterNotFound,

    /// <summary>デバイスの作成が E_OUTOFMEMORY（GPU の資源が尽きている→実装メモ5.82）。</summary>
    Exhausted,

    /// <summary>デバイスをそのほかの理由で作れない。</summary>
    Other,
}
