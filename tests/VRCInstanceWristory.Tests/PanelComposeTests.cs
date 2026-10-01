using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// パネルをオーバーレイ1枚に組み立てる（2026-09-23→実装メモ5.35）。
///
/// 2026-09-16から2026-09-22までは「枠」「一覧」「つまみ」…と14枚に分け、
/// 見た目の切り替えを切り出し範囲の変更だけで行っていた。これは <c>SetOverlayRaw</c> が
/// 渡すたびにちらつくことへの迂回で、<c>SetOverlayTexture</c> にした以上は要らない（→5.34）。
///
/// ここでは「1枚に全部入っていること」と「変わった部分だけが変わること」を確かめる。
/// </summary>
public class PanelComposeTests
{
    private static readonly PanelStyle Style = new();

    private static List<RowLayout> Sample(PanelRenderer renderer)
        => renderer.Measure(SampleRows.Build());

    [Fact]
    public void 行の下絵は最低でも表示領域ぶんの高さを持つ()
    {
        // 3行しかなくても、上詰めで見せるため表示領域ぶん確保する。
        Assert.Equal(Style.MaxViewportHeight, PanelGeometry.RowsTextureHeight(3 * 112f, Style.MaxViewportHeight));

        // 内容が長ければその高さ。
        Assert.Equal(1008, PanelGeometry.RowsTextureHeight(1008f, Style.MaxViewportHeight));
    }

    /// <summary>
    /// 見出し・表示領域・下部案内のどこにも「透明のまま残す場所」がない。
    /// 分けていた頃は、枠の表示領域を透明にして奥の一覧を透かしていた。
    /// </summary>
    [Fact]
    public void パネル1枚に見出しも行も下部案内も入る()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = Sample(renderer);
        renderer.RenderRows(layouts);

        var pixels = PanelPixels.Compose(renderer, 0f, new PanelDecorations { Countdown = "60:00" });

        // 渡す画素はいまの高さぴったり。
        Assert.Equal(Style.Width * renderer.Height * 4, pixels.Length);

        // どの画素も背景の不透明度より薄くならない（＝透明のまま残した場所がない）。
        // 文字や帯が乗っている画素はこれより濃くなる。
        // GDI+ は一度premultipliedへ通すので、文字の縁で2まで下振れすることがある
        // （帯の文言を「対象外のインスタンスへ移動」にして字の位置がずれたとき、2下振れする画素が出た→実装メモ5.76）。
        for (var y = 0; y < renderer.Height; y++)
        {
            for (var x = 0; x < renderer.Width; x++)
            {
                var alpha = PanelPixels.At(pixels, renderer.Width, x, y).A;
                Assert.True(alpha >= Style.BackgroundAlpha - 2, $"({x}, {y}) が透けている（{alpha}）");
            }
        }

        // 文字のない場所は背景の不透明度ちょうど。
        byte AlphaAt(int x, int y) => PanelPixels.At(pixels, renderer.Width, x, y).A;

        // 見出しは表題のすぐ右（右の文字が長くなっても空いている所）。
        Assert.Equal(Style.BackgroundAlpha, AlphaAt((int)renderer.HeaderTitleRect().Right + 12, Style.HeaderHeight / 2));
        Assert.Equal(Style.BackgroundAlpha, AlphaAt(2, Style.ViewportTop + (renderer.ViewportHeight / 2)));
        Assert.Equal(Style.BackgroundAlpha, AlphaAt(480, renderer.Height - (Style.FooterHeight / 2)));
    }

    /// <summary>スクロールしても、変わるのは表示領域とつまみだけ。</summary>
    [Fact]
    public void スクロールで変わるのは表示領域と溝だけ()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = Sample(renderer);
        renderer.RenderRows(layouts);

        var decorations = new PanelDecorations { Countdown = "60:00" };

        var top = PanelPixels.Compose(renderer, 0f, decorations);
        var scrolled = PanelPixels.Compose(renderer, Style.RowHeight, decorations);

        Assert.NotEqual(top, scrolled);

        // 見出しと下部案内はそのまま。
        var header = new RectangleF(0f, 0f, renderer.Width, Style.HeaderHeight);
        var footer = new RectangleF(0f, renderer.Height - Style.FooterHeight, renderer.Width, Style.FooterHeight);

        Assert.Equal(PanelPixels.Crop(top, renderer.Width, header), PanelPixels.Crop(scrolled, renderer.Width, header));
        Assert.Equal(PanelPixels.Crop(top, renderer.Width, footer), PanelPixels.Crop(scrolled, renderer.Width, footer));
    }

    /// <summary>同じ状態なら何度組み立てても同じ絵になる（＝渡し直す必要がない）。</summary>
    [Fact]
    public void 同じ状態なら同じ絵になる()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = Sample(renderer);
        renderer.RenderRows(layouts);

        var decorations = new PanelDecorations { Countdown = "12:34" };

        Assert.Equal(
            PanelPixels.Compose(renderer, 3f, decorations),
            PanelPixels.Compose(renderer, 3f, decorations));
    }

    /// <summary>行の下絵は、行が変わらなければ描き直さなくてよい（スクロールは窓を写すだけ）。</summary>
    [Fact]
    public void 行の下絵は描き直さずにスクロールできる()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = Sample(renderer);
        renderer.RenderRows(layouts);

        var rows = (byte[])renderer.RowsPixels().Clone();

        PanelPixels.Compose(renderer, 0f);
        PanelPixels.Compose(renderer, 240f);

        // 組み立てても下絵には触れない。
        Assert.Equal(rows, renderer.RowsPixels());
    }

    /// <summary>指している行の帯は表示領域の中だけに出て、溝には掛からない。</summary>
    [Fact]
    public void 指している行の帯は表示領域の中に収まる()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = Sample(renderer);
        renderer.RenderRows(layouts);

        var hit = renderer.RowAt(layouts, 1);
        var rect = PanelGeometry.RowHighlightRect(Style, renderer.ViewportHeight, 0f, hit.Top, hit.Height);

        var plain = PanelPixels.Compose(renderer, 0f, new PanelDecorations { Countdown = "60:00" });
        var hover = PanelPixels.Compose(renderer, 0f, new PanelDecorations { Countdown = "60:00", HoverRow = rect });

        Assert.NotEqual(
            PanelPixels.Crop(plain, renderer.Width, rect),
            PanelPixels.Crop(hover, renderer.Width, rect));

        // 帯の外は変わらない（溝・見出し・他の行を含む）。
        Assert.Equal(
            PanelPixels.Outside(plain, renderer.Width, renderer.Height, rect),
            PanelPixels.Outside(hover, renderer.Width, renderer.Height, rect));
    }

    /// <summary>目印のポップアップは行の上に重なって出る。</summary>
    [Fact]
    public void ポップアップは行の上に重なる()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = Sample(renderer);
        renderer.RenderRows(layouts);

        var hit = renderer.RowAt(layouts, 1);
        var row = PanelGeometry.RowHighlightRect(Style, renderer.ViewportHeight, 0f, hit.Top, hit.Height);
        var popup = PanelGeometry.MarkPopupRect(Style, renderer.ViewportHeight, row, Core.Marks.InstanceMarks.Choices.Count);

        var closed = PanelPixels.Compose(renderer, 0f, new PanelDecorations { Countdown = "60:00" });
        var open = PanelPixels.Compose(
            renderer,
            0f,
            new PanelDecorations
            {
                Countdown = "60:00",
                HoverRow = row,
                Popup = popup,
                PopupCurrent = Core.Marks.InstanceMark.Heart,
                PopupPointed = Core.Marks.InstanceMark.Warning,
            });

        Assert.NotEqual(
            PanelPixels.Crop(closed, renderer.Width, popup),
            PanelPixels.Crop(open, renderer.Width, popup));

        // ポップアップは表示領域の中に収まる。
        Assert.True(popup.Top >= Style.ViewportTop);
        Assert.True(popup.Bottom <= Style.ViewportTop + renderer.ViewportHeight);
    }
}
