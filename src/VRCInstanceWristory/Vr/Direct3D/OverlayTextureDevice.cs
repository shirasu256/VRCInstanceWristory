using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Vr.Direct3D;

/// <summary>
/// SteamVRが描画に使うアダプターの上に作るD3D11デバイス。
///
/// <c>SetOverlayRaw</c> は画素をSteamVRへ送り付ける仕組みで、送るたびに絵が一度外れる
/// （Valveも「頻繁に更新するものへ使うな」と明言している）。代わりに、こちらでテクスチャを持ち、
/// その入れ物を <c>SetOverlayTexture</c> で指し示すと、更新しても絵は外れない。
///
/// アダプターは SteamVR が描画に使うものでなければならない（<c>IVRSystem::GetOutputDevice</c> の LUID で探す→実装メモ5.85）。
/// 別のGPU上のテクスチャは <c>EVROverlayError.InvalidTexture</c> で拒まれる。
/// </summary>
public sealed class OverlayTextureDevice : IDisposable
{
    private readonly IDiagnostics _log;
    private nint _device;
    private nint _context;
    private bool _disposed;

    private OverlayTextureDevice(IDiagnostics log, nint device, nint context)
    {
        _log = log;
        _device = device;
        _context = context;
    }

    /// <summary>
    /// 作れなければ null を返す。呼び出し側は理由を出して起動を止める。
    ///
    /// アダプターは、SteamVR が教える GPU の LUID（<c>IVRSystem::GetOutputDevice</c>）で探す（→実装メモ5.85）。
    /// 番号（<c>GetDXGIOutputInfo</c>）は SteamVR のプロセスで数えたもので、切り替え式の GPU では
    /// Windows の「グラフィックの設定」によってこのプロセスとは並びが違うことがあるため。
    /// LUID で見つからないとき（取れなかったときを含む）だけ番号を使う。
    /// </summary>
    /// <param name="adapterLuid">SteamVR が使う GPU の LUID。取れなければ 0。</param>
    /// <param name="adapterIndex">SteamVR が使う GPU の番号。取れなければ -1。</param>
    /// <param name="failure">作れなかった理由（作れたときは <see cref="TextureDeviceFailure.None"/>）。</param>
    public static OverlayTextureDevice? TryCreate(IDiagnostics log, long adapterLuid, int adapterIndex, out TextureDeviceFailure failure)
    {
        failure = TextureDeviceFailure.None;

        var adapter = nint.Zero;
        var factory = nint.Zero;

        try
        {
            var iid = D3D11Interop.IidDxgiFactory1;
            var hr = D3D11Interop.CreateDXGIFactory1(ref iid, out factory);

            if (!D3D11Interop.Succeeded(hr))
            {
                log.Warn($"DXGIのfactoryを作れません（{D3D11Interop.Hr(hr)}）。");
                failure = TextureDeviceFailure.Unavailable;
                return null;
            }

            adapter = FindAdapter(log, factory, adapterLuid, adapterIndex, out var index);

            if (adapter == nint.Zero)
            {
                failure = TextureDeviceFailure.AdapterNotFound;
                return null;
            }

            hr = D3D11Interop.D3D11CreateDevice(
                adapter,
                D3D11Interop.DriverTypeUnknown,
                nint.Zero,
                D3D11Interop.CreateDeviceBgraSupport,
                nint.Zero,
                0,
                D3D11Interop.SdkVersion,
                out var device,
                out _,
                out var context);

            if (!D3D11Interop.Succeeded(hr) || device == nint.Zero || context == nint.Zero)
            {
                log.Warn($"D3D11デバイスを作れません（{D3D11Interop.Hr(hr)}）。");
                failure = hr == D3D11Interop.EOutOfMemory ? TextureDeviceFailure.Exhausted : TextureDeviceFailure.Other;
                return null;
            }

            var name = D3D11Interop.Succeeded(D3D11Interop.GetAdapterDesc(adapter, out var desc))
                ? desc.Description
                : $"{index}番";

            log.Info($"D3D11デバイスを作りました（SteamVRのアダプター: {name}）。テクスチャは SetOverlayTexture で渡します。");
            return new OverlayTextureDevice(log, device, context);
        }
        catch (DllNotFoundException ex)
        {
            log.Warn($"D3D11を読み込めません: {ex.Message}。");
            failure = TextureDeviceFailure.Unavailable;
            return null;
        }
        catch (EntryPointNotFoundException ex)
        {
            log.Warn($"D3D11の関数が見つかりません: {ex.Message}。");
            failure = TextureDeviceFailure.Unavailable;
            return null;
        }
        finally
        {
            if (adapter != nint.Zero)
                D3D11Interop.Release(adapter);

            if (factory != nint.Zero)
                D3D11Interop.Release(factory);
        }
    }

    /// <summary>
    /// SteamVR が使う GPU を探す。LUID が合うものを先に探し、なければ番号で取る（→実装メモ5.85）。
    /// 番号も取れない環境（HMD未接続など）では既定の0番へ落とす。見つからなければ 0 を返す。
    /// </summary>
    private static nint FindAdapter(IDiagnostics log, nint factory, long luid, int adapterIndex, out uint index)
    {
        if (luid != 0)
        {
            for (index = 0; ; index++)
            {
                if (!D3D11Interop.Succeeded(D3D11Interop.EnumAdapters(factory, index, out var candidate)) || candidate == nint.Zero)
                    break;

                if (D3D11Interop.Succeeded(D3D11Interop.GetAdapterDesc(candidate, out var desc)) && desc.AdapterLuid == luid)
                    return candidate;

                D3D11Interop.Release(candidate);
            }

            log.Warn($"SteamVR が使う GPU（LUID {luid:X16}）が見つかりません。番号で探します。");
        }

        index = (uint)(adapterIndex >= 0 ? adapterIndex : 0);
        var hr = D3D11Interop.EnumAdapters(factory, index, out var adapter);

        if (!D3D11Interop.Succeeded(hr) || adapter == nint.Zero)
        {
            log.Warn($"アダプター{index}番を取得できません（{D3D11Interop.Hr(hr)}）。");
            return nint.Zero;
        }

        return adapter;
    }

    /// <summary>
    /// GPU がリセットされる（ドライバーの TDR など）と、作ったデバイスは使えなくなる（→実装メモ5.85）。
    /// そのときは失われた理由（DXGI_ERROR_DEVICE_REMOVED など）を返す。使えていれば 0。
    /// </summary>
    public int RemovedReason => _disposed || _device == nint.Zero ? 0 : D3D11Interop.GetDeviceRemovedReason(_device);

    /// <summary>
    /// BGRAのテクスチャを1枚作る。CPU から書き込めるもの（<c>D3D11_USAGE_DYNAMIC</c>）にして、画素は <c>Map</c> で入れる。
    /// </summary>
    public OverlayTexture? TryCreateTexture(int width, int height)
    {
        if (_disposed || width <= 0 || height <= 0)
            return null;

        var desc = new D3D11Interop.Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = D3D11Interop.FormatB8G8R8A8Unorm,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = D3D11Interop.UsageDynamic,
            BindFlags = D3D11Interop.BindShaderResource,
            CpuAccessFlags = D3D11Interop.CpuAccessWrite,
            MiscFlags = 0,
        };

        var hr = D3D11Interop.CreateTexture2D(_device, ref desc, out var texture);

        if (!D3D11Interop.Succeeded(hr) || texture == nint.Zero)
        {
            _log.Error($"テクスチャ（{width}×{height}）を作れません: {D3D11Interop.Hr(hr)}");
            return null;
        }

        return new OverlayTexture(_context, texture, width, height);
    }

    /// <summary>積んだコマンドをGPUへ流す。転送直後に一度だけ呼ぶ。</summary>
    public void Flush()
    {
        if (!_disposed && _context != nint.Zero)
            D3D11Interop.Flush(_context);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_context != nint.Zero)
        {
            D3D11Interop.Release(_context);
            _context = nint.Zero;
        }

        if (_device != nint.Zero)
        {
            D3D11Interop.Release(_device);
            _device = nint.Zero;
        }
    }
}
