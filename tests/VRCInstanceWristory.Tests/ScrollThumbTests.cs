using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// スクロール位置を示すつまみ（2026-09-21のユーザー指定）。
///
/// 2026-09-22まではつまみを専用のオーバーレイにして、高さを切り出し範囲・位置を transform で
/// 変えていた。これは <c>SetOverlayRaw</c> のちらつきを避けるためのもので、
/// いまは溝もつまみもパネルへ直接描いている（→実装メモ5.35）。
/// </summary>
public class ScrollThumbTests
{
    private static readonly PanelStyle Style = new();

    private static RectangleF Track => PanelGeometry.ScrollTrackRect(Style, Style.MaxViewportHeight);

    [Fact]
    public void 溝は表示領域の右端に上下の余白を取って置く()
    {
        var track = Track;

        Assert.Equal(Style.Width - Style.ScrollBarWidth - 4f, track.X, 3);
        Assert.Equal(Style.ViewportTop + 4f, track.Y, 3);
        Assert.Equal(Style.ScrollBarWidth, track.Width, 3);
        Assert.Equal(Style.MaxViewportHeight - 8f, track.Height, 3);

        // 縦幅を詰めたときは、詰めたぶんだけ溝も短くなる。
        var shortened = PanelGeometry.ScrollTrackRect(Style, Style.RowHeight * 3);
        Assert.Equal((Style.RowHeight * 3) - 8f, shortened.Height, 3);
    }

    [Fact]
    public void つまみは内容が長いほど短くなる()
    {
        var track = Track;

        // 15行（表示領域のちょうど2倍）なら溝の半分。
        var half = PanelGeometry.ScrollThumbRect(Style, Style.MaxViewportHeight, Style.MaxViewportHeight * 2f, 0f);
        Assert.Equal(track.Height / 2f, half.Height, 3);

        // 内容が長いほど短い。
        var quarter = PanelGeometry.ScrollThumbRect(Style, Style.MaxViewportHeight, Style.MaxViewportHeight * 4f, 0f);
        Assert.True(quarter.Height < half.Height);
    }

    [Fact]
    public void つまみは短くなりすぎない()
    {
        // 500行ぶんでも、手首の距離で見失わないよう最小の高さで止める。
        var thumb = PanelGeometry.ScrollThumbRect(Style, Style.MaxViewportHeight, Style.RowHeight * 500f, 0f);

        Assert.Equal(PanelGeometry.MinThumbHeight, thumb.Height, 3);
    }

    [Fact]
    public void つまみは上端から下端まで動く()
    {
        var track = Track;
        var content = Style.MaxViewportHeight * 2f;
        var maxOffset = content - Style.MaxViewportHeight;

        var top = PanelGeometry.ScrollThumbRect(Style, Style.MaxViewportHeight, content, 0f);
        Assert.Equal(track.Y, top.Y, 3);

        var middle = PanelGeometry.ScrollThumbRect(Style, Style.MaxViewportHeight, content, maxOffset / 2f);
        Assert.Equal(track.Y + ((track.Height - middle.Height) / 2f), middle.Y, 3);

        var bottom = PanelGeometry.ScrollThumbRect(Style, Style.MaxViewportHeight, content, maxOffset);
        Assert.Equal(track.Bottom, bottom.Bottom, 3);

        // 範囲の外へは出ない。
        var below = PanelGeometry.ScrollThumbRect(Style, Style.MaxViewportHeight, content, maxOffset * 10f);
        Assert.Equal(bottom.Y, below.Y, 3);

        var above = PanelGeometry.ScrollThumbRect(Style, Style.MaxViewportHeight, content, -500f);
        Assert.Equal(track.Y, above.Y, 3);
    }

    [Fact]
    public void 内容が収まるときつまみは溝いっぱいになる()
    {
        var track = Track;
        var thumb = PanelGeometry.ScrollThumbRect(Style, Style.MaxViewportHeight, Style.MaxViewportHeight, 0f);

        Assert.Equal(track.Height, thumb.Height, 3);
    }

    /// <summary>
    /// つまみはパネルに直接描く。2026-09-23までは転送を避けるため別オーバーレイだった（→実装メモ5.35）。
    /// </summary>
    [Fact]
    public void つまみは溝の中に描かれスクロールで動く()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = renderer.Measure(SampleRows.Build());
        renderer.RenderRows(layouts);

        var content = layouts.Sum(l => l.Height);
        Assert.True(content > renderer.ViewportHeight, "スクロールできる行数の見本のはず");

        var top = PanelPixels.Compose(renderer);
        var bottom = PanelPixels.Compose(renderer, content - renderer.ViewportHeight);

        var track = PanelGeometry.ScrollTrackRect(Style, renderer.ViewportHeight);
        var x = (int)track.X + (Style.ScrollBarWidth / 2);

        Color Thumb(byte[] pixels, int y) => PanelPixels.At(pixels, renderer.Width, x, y);

        var near = (int)track.Y + 2;
        var far = (int)track.Bottom - 3;

        // 先頭では溝の上のほうが濃く、末尾では下のほうが濃い。
        Assert.True(Thumb(top, near).A > Thumb(top, far).A, "先頭ではつまみが上にあるはず");
        Assert.True(Thumb(bottom, far).A > Thumb(bottom, near).A, "末尾ではつまみが下にあるはず");

        // つまみの色は溝と同じ（濃さだけが違う）。
        Assert.Equal(Style.ScrollTrack.ToArgb(), Color.FromArgb(255, Thumb(top, near)).ToArgb());
    }

    /// <summary>行が収まっているときは溝もつまみも出さない。</summary>
    [Fact]
    public void 行が収まるときは溝を描かない()
    {
        using var renderer = new PanelRenderer(Style);

        var rows = SampleRows.Build().Take(2).ToList();
        var layouts = renderer.Measure(rows);
        renderer.SetViewportForContent(layouts.Sum(l => l.Height));
        renderer.RenderRows(layouts);

        var pixels = PanelPixels.Compose(renderer);
        var track = PanelGeometry.ScrollTrackRect(Style, renderer.ViewportHeight);
        var x = (int)track.X + (Style.ScrollBarWidth / 2);

        // 溝の位置も、表示領域の他の場所と同じ背景のまま。
        var groove = PanelPixels.At(pixels, renderer.Width, x, (int)track.Y + 4);
        var plain = PanelPixels.At(pixels, renderer.Width, 8, (int)track.Y + 4);

        Assert.Equal(plain.ToArgb(), groove.ToArgb());
    }
}
