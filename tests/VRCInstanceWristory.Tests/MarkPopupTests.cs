using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 指している行の帯と、目印を選ぶポップアップ（2026-09-22のユーザー指定→実装メモ5.32）。
///
/// どちらもパネルへ直接描く。2026-09-22までは専用のオーバーレイに分けていたが、
/// それは <c>SetOverlayRaw</c> のちらつきを避けるための作りだった（→実装メモ5.35）。
/// </summary>
public class MarkPopupTests
{
    private static readonly PanelStyle Style = new();

    private static List<RowLayout> SampleLayouts(PanelRenderer renderer)
        => renderer.Measure(SampleRows.Build());

    [Fact]
    public void 指した位置から行を求める()
    {
        using var renderer = new PanelRenderer(Style);
        var layouts = SampleLayouts(renderer);

        // 1行目の真ん中。
        Assert.True(renderer.TryHitRow(layouts, layouts[0].Height / 2f, out var first));
        Assert.Equal(0, first.Index);
        Assert.Equal(0f, first.Top, 3);

        // 2行目の先頭。
        Assert.True(renderer.TryHitRow(layouts, layouts[0].Height + 1f, out var second));
        Assert.Equal(1, second.Index);

        // 行の外（最後の行より下）はどの行でもない。
        var total = layouts.Sum(l => l.Height);
        Assert.False(renderer.TryHitRow(layouts, total + 10f, out _));
        Assert.False(renderer.TryHitRow(layouts, -1f, out _));
    }

    /// <summary>
    /// 行と行の間の帯（「対象外のインスタンスへ移動」・クラッシュ）は行ではない。
    /// そこを指している間はどの行も指していないことにする。
    /// </summary>
    [Fact]
    public void 行の間の帯はどの行でもない()
    {
        using var renderer = new PanelRenderer(Style);
        var layouts = SampleLayouts(renderer);

        var banded = layouts.FindIndex(l => l.Height > Style.RowHeight);
        Assert.True(banded > 0, "見本には帯の付いた行があるはず");

        var top = 0f;
        for (var i = 0; i < banded; i++)
            top += layouts[i].Height;

        // 帯の中は行ではない。
        Assert.False(renderer.TryHitRow(layouts, top + 2f, out _));

        // 帯のすぐ下からがその行。
        Assert.True(renderer.TryHitRow(layouts, top + Style.AbsenceBandHeight + 2f, out var hit));
        Assert.Equal(banded, hit.Index);

        // 番号から引いても同じ位置になる。
        var byIndex = renderer.RowAt(layouts, banded);
        Assert.Equal(hit.Top, byIndex.Top, 3);
        Assert.Equal(hit.Height, byIndex.Height, 3);
        Assert.Equal(Style.RowHeight, byIndex.Height, 3);
    }

    [Fact]
    public void 帯は表示領域からはみ出した分を切り落とす()
    {
        // 表示領域の中ほどの行はそのまま。
        var middle = PanelGeometry.RowHighlightRect(Style, Style.MaxViewportHeight, 0f, Style.RowHeight, Style.RowHeight);
        Assert.Equal(Style.ViewportTop + Style.RowHeight, middle.Y, 3);
        Assert.Equal(Style.RowHeight, middle.Height, 3);

        // 上へスクロールして半分隠れた行は、見えている分だけ。
        var clipped = PanelGeometry.RowHighlightRect(Style, Style.MaxViewportHeight, Style.RowHeight * 1.5f, Style.RowHeight, Style.RowHeight);
        Assert.Equal(Style.ViewportTop, clipped.Y, 3);
        Assert.Equal(Style.RowHeight / 2f, clipped.Height, 3);

        // 表示領域の外の行は高さ0。
        var outside = PanelGeometry.RowHighlightRect(Style, Style.RowHeight * 2, 0f, Style.RowHeight * 5, Style.RowHeight);
        Assert.Equal(0f, outside.Height, 3);

        // 溝とは重ならない幅にする（同じ手前面に置くので、重ねると前後が決まらない）。
        Assert.True(middle.Right <= PanelGeometry.ScrollTrackRect(Style, Style.MaxViewportHeight).X);
    }

    [Fact]
    public void 指した点を画素へ直す()
    {
        const float width = 0.14f;
        var height = Style.MaxHeight;

        // 中心は枠の真ん中。
        var center = PanelGeometry.PixelFromPanelLocal(Style, width, height, 0f, 0f);
        Assert.Equal(Style.Width / 2f, center.X, 3);
        Assert.Equal(height / 2f, center.Y, 3);

        // 右と上へ動かすと、xは増えyは減る（テクスチャのyは下向き）。
        var metersPerPixel = width / Style.Width;
        var moved = PanelGeometry.PixelFromPanelLocal(Style, width, height, metersPerPixel * 100f, metersPerPixel * 50f);

        Assert.Equal((Style.Width / 2f) + 100f, moved.X, 3);
        Assert.Equal((height / 2f) - 50f, moved.Y, 3);
    }

    [Fact]
    public void ポップアップは指している行に重ねて中央へ置く()
    {
        var choices = InstanceMarks.Choices.Count;
        var size = PanelGeometry.MarkPopupSize(choices);

        var row = PanelGeometry.RowHighlightRect(Style, Style.MaxViewportHeight, 0f, Style.RowHeight * 3, Style.RowHeight);
        var popup = PanelGeometry.MarkPopupRect(Style, Style.MaxViewportHeight, row, choices);

        // 左右は帯の中央、上下は行の中央。
        Assert.Equal((PanelGeometry.RowHighlightRight(Style) - size.Width) / 2f, popup.X, 3);
        Assert.Equal(row.Y + ((row.Height - size.Height) / 2f), popup.Y, 3);

        // 選択肢は等間隔に並ぶ。
        var first = PanelGeometry.MarkChoiceRect(popup, 0);
        var second = PanelGeometry.MarkChoiceRect(popup, 1);

        Assert.Equal(popup.X + PanelGeometry.MarkPopupPadding, first.X, 3);
        Assert.Equal(PanelGeometry.MarkChoiceSize, first.Width, 3);
        Assert.Equal(first.Right + PanelGeometry.MarkChoiceGap, second.X, 3);

        // 目印の右に区切りを挟んで「ここへ戻る」が付き、ポップアップの右端はその右端（→実装メモ5.43）。
        var back = PanelGeometry.ReturnChoiceRect(popup);
        Assert.Equal(PanelGeometry.MarkChoiceRect(popup, choices - 1).Right + PanelGeometry.ReturnChoiceGap, back.X, 3);
        Assert.Equal(popup.Right - PanelGeometry.MarkPopupPadding, back.Right, 3);
        Assert.Equal(PanelGeometry.MarkChoiceSize, back.Height, 3);
    }

    [Fact]
    public void ポップアップは表示領域の中へ収める()
    {
        var choices = InstanceMarks.Choices.Count;

        // いちばん上の行でも、いちばん下の行でも表示領域からはみ出さない。
        var top = PanelGeometry.MarkPopupRect(
            Style,
            Style.MaxViewportHeight,
            PanelGeometry.RowHighlightRect(Style, Style.MaxViewportHeight, 0f, 0f, Style.RowHeight),
            choices);

        var bottom = PanelGeometry.MarkPopupRect(
            Style,
            Style.MaxViewportHeight,
            PanelGeometry.RowHighlightRect(Style, Style.MaxViewportHeight, 0f, Style.MaxViewportHeight - Style.RowHeight, Style.RowHeight),
            choices);

        Assert.True(top.Y > Style.ViewportTop);
        Assert.True(bottom.Bottom < Style.ViewportTop + Style.MaxViewportHeight);
    }

    /// <summary>「わずかに白く」なる程度に留める（行の文字は読めるまま）。</summary>
    [Fact]
    public void 帯は薄い白()
    {
        Assert.Equal(Color.White.ToArgb(), Color.FromArgb(255, Style.RowHighlight).ToArgb());
        Assert.InRange(Style.RowHighlightAlpha, 8, 40);
    }

    /// <summary>目印はインスタンス番号の後ろ（ID列の中）に描く。付加情報の列へは食い込ませない。</summary>
    [Fact]
    public void 目印はID列の中に描く()
    {
        using var renderer = new PanelRenderer(Style);

        var rows = SampleRows.Build();
        Assert.Contains(rows, r => r.Mark != InstanceMark.None);

        var layouts = renderer.Measure(rows);
        renderer.RenderRows(layouts);

        var pixels = renderer.RowsPixels();
        var heart = Style.MarkHeart;

        var found = false;
        var beyond = false;

        for (var y = 0; y < layouts.Sum(l => (int)l.Height); y++)
        {
            for (var x = 0; x < Style.Width; x++)
            {
                var i = ((y * Style.Width) + x) * 4;

                if (pixels[i + 0] != heart.B || pixels[i + 1] != heart.G || pixels[i + 2] != heart.R)
                    continue;

                found = true;

                if (x < Style.IdColumnLeft || x > Style.IdColumnLeft + Style.IdColumnWidth)
                    beyond = true;
            }
        }

        Assert.True(found, "見本にはハートの目印が描かれているはず");
        Assert.False(beyond, "目印はID列の中に収まるはず");
    }
}
