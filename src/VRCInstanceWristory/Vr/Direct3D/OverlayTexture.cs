using Valve.VR;

namespace VRCInstanceWristory.Vr.Direct3D;

/// <summary>
/// 1枚のテクスチャ。<c>SetOverlayTexture</c> へ渡す入れ物になる。
/// デバイスの文脈（<paramref name="context"/>）の参照は持たないので、作った <see cref="OverlayTextureDevice"/> より先に破棄する
/// （<see cref="VrOverlay.Destroy"/> がセッションの終了より前に行う）。
/// </summary>
public sealed class OverlayTexture(nint context, nint texture, int width, int height) : IDisposable
{
    private nint _texture = texture;

    public int Width => width;

    public int Height => height;

    /// <summary><c>SetOverlayTexture</c> へ渡す記述子。中身を書き換えても指し先は変わらない。</summary>
    public Texture_t Descriptor { get; } = new()
    {
        handle = texture,
        eType = ETextureType.DirectX,

        // Auto にすると 8bit の UNORM はガンマとして扱われる（GDI+ で描いた色をそのまま出す）。
        eColorSpace = EColorSpace.Auto,
    };

    /// <summary>
    /// BGRAの画素を書き込む。<paramref name="pixels"/> の先頭から
    /// <see cref="Height"/> 行ぶんだけを使う（面より低い絵を渡すことはない）。
    /// </summary>
    public unsafe bool Write(byte[] pixels)
    {
        if (_texture == nint.Zero)
            return false;

        var rowBytes = width * 4;
        var needed = (long)rowBytes * height;

        if (pixels.Length < needed)
            return false;

        fixed (byte* source = pixels)
        {
            var hr = D3D11Interop.Map(context, _texture, 0, D3D11Interop.MapWriteDiscard, 0, out var mapped);

            if (!D3D11Interop.Succeeded(hr) || mapped.Data == nint.Zero)
                return false;

            try
            {
                var destination = (byte*)mapped.Data;
                var pitch = (int)mapped.RowPitch;

                if (pitch == rowBytes)
                {
                    Buffer.MemoryCopy(source, destination, needed, needed);
                }
                else
                {
                    // 行の間に余白が入る場合は1行ずつ写す。
                    for (var y = 0; y < height; y++)
                        Buffer.MemoryCopy(source + ((long)y * rowBytes), destination + ((long)y * pitch), rowBytes, rowBytes);
                }
            }
            finally
            {
                D3D11Interop.Unmap(context, _texture, 0);
            }

            return true;
        }
    }

    public void Dispose()
    {
        if (_texture == nint.Zero)
            return;

        D3D11Interop.Release(_texture);
        _texture = nint.Zero;
    }
}
