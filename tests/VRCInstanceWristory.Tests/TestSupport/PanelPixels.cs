using System.Drawing;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 組み立てたパネルの画素を読むための共通処理。
///
/// パネルは2026-09-23からオーバーレイ1枚に全部を描くので、見出し・行・つまみ・
/// 指している行の帯・目印のポップアップは、どれも同じ画素列の中にある（→実装メモ5.35）。
/// 画素の並びは BGRA（D3D11 の B8G8R8A8_UNORM と同じ）。
/// </summary>
public static class PanelPixels
{
    /// <summary>行の下絵を描き直してから、飾りなし（残り時間もボタンもない見出し）で1枚に組み立てる。</summary>
    public static void Render(this PanelRenderer renderer, IReadOnlyList<RowLayout> layouts, float scrollOffset)
    {
        renderer.RenderRows(layouts);
        renderer.Compose(scrollOffset);
    }

    /// <summary>パネルを組み立てて、その画素の写しを返す。</summary>
    public static byte[] Compose(PanelRenderer renderer, float scrollOffset = 0f, PanelDecorations decorations = default)
    {
        renderer.Compose(scrollOffset, decorations);
        return (byte[])renderer.GetPixels().Clone();
    }

    /// <summary>(x, y) の色。</summary>
    public static Color At(byte[] pixels, int width, int x, int y)
    {
        var i = ((y * width) + x) * 4;
        return Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i + 0]);
    }

    /// <summary>矩形の中の画素だけを取り出す。</summary>
    public static byte[] Crop(byte[] pixels, int width, RectangleF rect)
    {
        var x0 = Math.Max(0, (int)MathF.Floor(rect.X));
        var y0 = Math.Max(0, (int)MathF.Floor(rect.Y));
        var x1 = (int)MathF.Ceiling(rect.Right);
        var y1 = (int)MathF.Ceiling(rect.Bottom);

        var cropped = new List<byte>((x1 - x0) * (y1 - y0) * 4);

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = ((y * width) + x) * 4;
                cropped.AddRange(pixels.AsSpan(i, 4).ToArray());
            }
        }

        return [.. cropped];
    }

    /// <summary>矩形の外の画素をまとめて取り出す（外側が変わっていないことの確認に使う）。</summary>
    public static byte[] Outside(byte[] pixels, int width, int height, RectangleF rect)
    {
        var outside = new List<byte>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (rect.Contains(x, y))
                    continue;

                var i = ((y * width) + x) * 4;
                outside.AddRange(pixels.AsSpan(i, 4).ToArray());
            }
        }

        return [.. outside];
    }
}
