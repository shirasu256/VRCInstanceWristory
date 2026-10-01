using System.Diagnostics;
using System.Runtime.InteropServices;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr.Direct3D;
using Valve.VR;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 1枚のオーバーレイ。可視性は呼び出し側で一元的に管理する。
///
/// 表示・不透明度・幅は最後に通した値を覚えておき、同じ値ならSteamVRへ投げ直さない（毎フレーム呼んでよい）。
/// 失敗したときは覚えないので、次に呼ばれたときに試し直す。
/// 失敗のログは操作ごとに間引く（<see cref="FailureLogInterval"/>）。90Hzで試し直すたびに出すとログが埋まるため。
/// </summary>
public sealed class VrOverlay(ulong handle, string key, IDiagnostics log, OverlayTextureDevice textureDevice)
{
    /// <summary>
    /// 不透明度を実際に反映する最小の差。毎フレーム同じ値を投げ直さないためだけの閾値で、
    /// 8bitの1段よりも細かく取ってある（見た目が変わる変化は必ず通す）。
    /// </summary>
    private const float AlphaEpsilon = 1f / 512f;

    /// <summary>同じ操作の失敗を続けてログへ出す間隔。最初の1回はすぐに出し、あとはこの間隔で回数をまとめて出す。</summary>
    public static readonly TimeSpan FailureLogInterval = TimeSpan.FromSeconds(5);

    private bool _visible;
    private bool _destroyed;
    private OverlayTexture? _texture;
    private float _alpha = float.NaN;
    private float _widthMeters = float.NaN;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly LogThrottle _failureLog = new(FailureLogInterval);

    public bool Visible => _visible;

    /// <summary>直近のテクスチャ転送に成功しているか。失敗を表示成功として扱わない。</summary>
    public bool TextureReady
    {
        get => _textureReady;
        private set
        {
            _textureReady = value;
            ConsecutiveFailures = value ? 0 : ConsecutiveFailures + 1;

            if (value)
                TextureCreateFailed = false;
        }
    }

    private bool _textureReady;

    /// <summary>絵を渡せないことが続いた回数（→実装メモ5.85）。渡せたら0へ戻す。</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>直近の失敗が、テクスチャ（入れ物）を作れなかったことによるものか（→実装メモ5.85）。</summary>
    public bool TextureCreateFailed { get; private set; }

    /// <summary>物理的な幅（m）。前に通した値と同じなら何もしない。</summary>
    public bool SetWidthInMeters(float meters)
    {
        if (meters == _widthMeters)
            return true;

        if (!Check(OpenVR.Overlay.SetOverlayWidthInMeters(handle, meters), nameof(SetWidthInMeters)))
            return false;

        _widthMeters = meters;
        return true;
    }

    /// <summary>
    /// 不透明度。<c>SetOverlayAlpha</c> は絵を渡し直さないのでちらつかない。
    /// 前に通した値との差が <see cref="AlphaEpsilon"/> 未満なら何もしない（見た目が変わらない差は捨てる）。
    /// </summary>
    public bool SetAlpha(float alpha)
    {
        if (!float.IsNaN(_alpha) && MathF.Abs(_alpha - alpha) < AlphaEpsilon)
            return true;

        if (!Check(OpenVR.Overlay.SetOverlayAlpha(handle, alpha), nameof(SetAlpha)))
            return false;

        _alpha = alpha;
        return true;
    }

    public bool SetSortOrder(uint order)
        => Check(OpenVR.Overlay.SetOverlaySortOrder(handle, order), nameof(SetSortOrder));

    public bool SetInputMethodNone()
        => Check(OpenVR.Overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.None), nameof(SetInputMethodNone));

    /// <summary>
    /// ダッシュボードのレーザーでマウスのように操作させる（→実装メモ5.40）。
    /// 座標は <paramref name="width"/>×<paramref name="height"/> の画素で届く（左下が原点）。
    /// </summary>
    public bool SetMouseInput(int width, int height)
    {
        var scale = new HmdVector2_t { v0 = width, v1 = height };

        return Check(OpenVR.Overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.Mouse), nameof(SetMouseInput))
            && Check(OpenVR.Overlay.SetOverlayMouseScale(handle, ref scale), nameof(SetMouseInput));
    }

    /// <summary>このオーバーレイに届いたイベントを1つ取り出す（マウス・表示の切り替えなど）。</summary>
    public bool PollEvent(ref VREvent_t ev)
        => !_destroyed && OpenVR.Overlay.PollNextOverlayEvent(handle, ref ev, (uint)Marshal.SizeOf<VREvent_t>());

    /// <summary>SteamVR側で見えているか。ダッシュボードのオーバーレイはSteamVRが出し入れするので、こちらの <see cref="Visible"/> とは別。</summary>
    public bool ShownBySteamVr => !_destroyed && OpenVR.Overlay.IsOverlayVisible(handle);

    public bool SetTransformDeviceRelative(uint deviceIndex, HmdMatrix34_t transform)
        => Check(
            OpenVR.Overlay.SetOverlayTransformTrackedDeviceRelative(handle, deviceIndex, ref transform),
            nameof(SetTransformDeviceRelative));

    public bool SetTransformAbsolute(ETrackingUniverseOrigin origin, HmdMatrix34_t transform)
        => Check(
            OpenVR.Overlay.SetOverlayTransformAbsolute(handle, origin, ref transform),
            nameof(SetTransformAbsolute));

    /// <summary>
    /// 絵を差し替える。失敗したら TextureReady を false にする。
    ///
    /// こちらが持つD3D11のテクスチャへ書いてから <c>SetOverlayTexture</c> でそれを指す。
    /// SteamVRは同じ入れ物を見続けるので、書き換えても絵がいったん外れることがない
    /// （<c>SetOverlayRaw</c> は送るたびに絵が外れてちらつく→実装メモ5.34）。
    /// </summary>
    public bool SetTexture(byte[] pixels, int width, int height)
    {
        // 大きさが変わったときだけ作り直す。作り直しはSteamVRから見て入れ物の差し替えなので、
        // 行数が変わったとき（＝インスタンスを移ったとき）以外では起きない。
        if (_texture is null || _texture.Width != width || _texture.Height != height)
        {
            _texture?.Dispose();
            _texture = textureDevice.TryCreateTexture(width, height);

            if (_texture is null)
            {
                TextureReady = false;
                TextureCreateFailed = true;
                return false;
            }
        }

        if (!_texture.Write(pixels))
        {
            LogFailure("Write", $"テクスチャへ書き込めませんでした（{width}×{height}）。");
            TextureReady = false;
            TextureCreateFailed = false;
            return false;
        }

        var descriptor = _texture.Descriptor;
        var error = OpenVR.Overlay.SetOverlayTexture(handle, ref descriptor);
        TextureReady = error == EVROverlayError.None;

        // 書き込みとSteamVR側の複写を、ここでGPUへ流しきる。
        // 溜めたままにすると、まれに古い中身が1フレーム出る（openvr の課題 #1353）。
        // 流すのは SetOverlayTexture の「あと」でなければ意味がない。複写はその中で積まれるため。
        textureDevice.Flush();

        if (!TextureReady)
        {
            TextureCreateFailed = false;
            LogFailure("SetOverlayTexture", $"SetOverlayTexture に失敗しました（{width}×{height}）: {error}");
        }

        return TextureReady;
    }

    public bool TryComputeIntersection(
        ETrackingUniverseOrigin origin,
        System.Numerics.Vector3 source,
        System.Numerics.Vector3 direction,
        out VROverlayIntersectionResults_t results)
    {
        var parameters = new VROverlayIntersectionParams_t
        {
            vSource = Math3d.ToHmd(source),
            vDirection = Math3d.ToHmd(direction),
            eOrigin = origin,
        };

        results = default;
        return OpenVR.Overlay.ComputeOverlayIntersection(handle, ref parameters, ref results);
    }

    public void SetVisible(bool visible)
    {
        if (_destroyed || _visible == visible)
            return;

        var error = visible
            ? OpenVR.Overlay.ShowOverlay(handle)
            : OpenVR.Overlay.HideOverlay(handle);

        if (error != EVROverlayError.None)
        {
            var operation = visible ? "ShowOverlay" : "HideOverlay";
            LogFailure(operation, $"{operation} に失敗しました: {error}");
            return;
        }

        _visible = visible;
    }

    public void Destroy()
    {
        if (_destroyed)
            return;

        OpenVR.Overlay.HideOverlay(handle);

        // テクスチャを離す前に、SteamVR側の参照を切っておく。
        if (_texture is not null)
            OpenVR.Overlay.ClearOverlayTexture(handle);

        OpenVR.Overlay.DestroyOverlay(handle);

        _texture?.Dispose();
        _texture = null;
        _destroyed = true;
        _visible = false;
    }

    private bool Check(EVROverlayError error, string operation)
    {
        if (error == EVROverlayError.None)
            return true;

        LogFailure(operation, $"{operation} に失敗しました: {error}");
        return false;
    }

    /// <summary>
    /// 失敗をログへ出す。同じ操作の失敗は <see cref="FailureLogInterval"/> に1回だけ出し、その間に出さなかった回数を添える。
    /// 呼び出し側は失敗しても毎フレーム試し直すので（配置・表示の切り替えなど）、そのたびに出すとログが埋まる。
    /// </summary>
    private void LogFailure(string operation, string message)
    {
        if (!_failureLog.ShouldLog(operation, _clock.Elapsed, out var suppressed))
            return;

        var repeated = suppressed > 0 ? $"（前回のログから同じ失敗がほかに{suppressed}回）" : string.Empty;
        log.Error($"{key}: {message}{repeated}");
    }
}
