using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VRCInstanceWristory.Vr.Direct3D;

/// <summary>
/// D3D11 と DXGI のうち、オーバーレイのテクスチャを作るのに要るぶんだけの相互運用。
///
/// 使うのは「アダプターを選ぶ」「デバイスを作る」「2Dテクスチャを作る」「画素を書く」の4つだけなので、
/// ラッパーのライブラリは入れず、
/// COMの仮想関数表を直接呼ぶ。openvr_api.cs と同じく、依存を増やさない方針に合わせている。
///
/// 仮想関数表の並びは Windows SDK の d3d11.h / dxgi.h の宣言順そのもので、
/// 下の Slot の値はその順番を数えたもの。並びはインターフェイスごとに固定されており、
/// 後から挿入されることはない（挿入するとABIが壊れるため）。
/// </summary>
internal static unsafe partial class D3D11Interop
{
    /// <summary>ID3D11Device の仮想関数表の位置（d3d11.h の宣言順）。</summary>
    private static class DeviceSlot
    {
        public const int CreateTexture2D = 5;
        public const int GetDeviceRemovedReason = 39;
    }

    /// <summary>ID3D11DeviceContext の仮想関数表の位置。</summary>
    private static class ContextSlot
    {
        public const int Map = 14;
        public const int Unmap = 15;
        public const int Flush = 111;
    }

    /// <summary>IDXGIFactory の仮想関数表の位置。</summary>
    private static class FactorySlot
    {
        public const int EnumAdapters = 7;
    }

    /// <summary>IDXGIAdapter の仮想関数表の位置。</summary>
    private static class AdapterSlot
    {
        public const int GetDesc = 8;
    }

    /// <summary>IUnknown の仮想関数表の位置。</summary>
    private static class UnknownSlot
    {
        public const int Release = 2;
    }

    // d3d11.h / dxgi.h の定数。使うものだけを写した。
    public const uint DriverTypeUnknown = 0;
    public const uint CreateDeviceBgraSupport = 0x20;
    public const uint SdkVersion = 7;

    public const uint FormatB8G8R8A8Unorm = 87;

    public const uint UsageDynamic = 2;

    public const uint BindShaderResource = 0x8;

    public const uint CpuAccessWrite = 0x10000;

    public const uint MapWriteDiscard = 4;

    public static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [LibraryImport("d3d11.dll")]
    public static partial int D3D11CreateDevice(
        nint adapter,
        uint driverType,
        nint software,
        uint flags,
        nint featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out nint device,
        out uint featureLevel,
        out nint immediateContext);

    [LibraryImport("dxgi.dll")]
    public static partial int CreateDXGIFactory1(ref Guid riid, out nint factory);

    /// <summary>D3D11_TEXTURE2D_DESC。SampleDesc は2つのuintへ展開してある。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    /// <summary>D3D11_MAPPED_SUBRESOURCE。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MappedSubresource
    {
        public nint Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    /// <summary>DXGI_ADAPTER_DESC。診断ログにアダプター名を出すのと、SteamVR の GPU を LUID で探すのに使う（→実装メモ5.85）。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct AdapterDesc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
    }

    /// <summary>仮想関数表から <paramref name="slot"/> 番目の関数のアドレスを取り出す。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* Method(nint self, int slot) => (*(void***)self)[slot];

    public static uint Release(nint self)
        => ((delegate* unmanaged[Stdcall]<nint, uint>)Method(self, UnknownSlot.Release))(self);

    public static int EnumAdapters(nint factory, uint index, out nint adapter)
    {
        fixed (nint* target = &adapter)
            return ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Method(factory, FactorySlot.EnumAdapters))(factory, index, target);
    }

    public static int GetAdapterDesc(nint adapter, out AdapterDesc desc)
    {
        // 文字列を含むので、いったん生のバイト列で受けてからマーシャリングする。
        // DXGI_ADAPTER_DESC は x64 で 304 バイト。余裕を見て確保する。
        var buffer = stackalloc byte[512];
        var result = ((delegate* unmanaged[Stdcall]<nint, byte*, int>)Method(adapter, AdapterSlot.GetDesc))(adapter, buffer);

        desc = result >= 0
            ? Marshal.PtrToStructure<AdapterDesc>((nint)buffer)
            : default;

        return result;
    }

    public static int CreateTexture2D(nint device, ref Texture2DDesc desc, out nint texture)
    {
        fixed (Texture2DDesc* description = &desc)
        fixed (nint* target = &texture)
            return ((delegate* unmanaged[Stdcall]<nint, Texture2DDesc*, void*, nint*, int>)Method(device, DeviceSlot.CreateTexture2D))(
                device, description, null, target);
    }

    /// <summary>デバイスが失われていれば、その理由（DXGI_ERROR_DEVICE_REMOVED など）。失われていなければ S_OK（0）。</summary>
    public static int GetDeviceRemovedReason(nint device)
        => ((delegate* unmanaged[Stdcall]<nint, int>)Method(device, DeviceSlot.GetDeviceRemovedReason))(device);

    public static int Map(nint context, nint resource, uint subresource, uint mapType, uint mapFlags, out MappedSubresource mapped)
    {
        fixed (MappedSubresource* target = &mapped)
            return ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, MappedSubresource*, int>)Method(context, ContextSlot.Map))(
                context, resource, subresource, mapType, mapFlags, target);
    }

    public static void Unmap(nint context, nint resource, uint subresource)
        => ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Method(context, ContextSlot.Unmap))(context, resource, subresource);

    public static void Flush(nint context)
        => ((delegate* unmanaged[Stdcall]<nint, void>)Method(context, ContextSlot.Flush))(context);

    /// <summary>HRESULT が成功かどうか。</summary>
    public static bool Succeeded(int hr) => hr >= 0;

    /// <summary>ログに出すためのHRESULT表記。</summary>
    public static string Hr(int hr) => $"0x{hr:X8}";

    /// <summary>E_OUTOFMEMORY。GPU の資源が尽きていると D3D11CreateDevice がこれを返す（→実装メモ5.82）。</summary>
    public const int EOutOfMemory = unchecked((int)0x8007000E);
}
