using System.Drawing;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// パネルの中の寸法計算。座標はすべて「パネルのテクスチャ内のpx」で、
/// 描くときも右手のレイの命中を見るときも同じ値を使う。
/// パネルはオーバーレイ1枚に描くので、ここにあるのは純粋なレイアウトの計算だけである（→実装メモ5.35）。
/// </summary>
public static class PanelGeometry
{
    /// <summary>行の下絵の高さ（px）。表示領域より短い内容でも、上詰めで見せるため最低でも表示領域ぶん確保する。</summary>
    public static int RowsTextureHeight(float contentHeight, int viewportHeight)
        => Math.Max(viewportHeight, (int)MathF.Ceiling(contentHeight));

    /// <summary>
    /// 行が少なくて縦幅を詰めたぶん、パネルの中心を下げる量（m）。
    ///
    /// オーバーレイは中心を基準に置かれるので、高さが変わると上下へ同じだけ伸び縮みする。
    /// それでは手首に合わせた下端が動いてしまうため、下端を上限の高さのときと同じ位置に留め、
    /// 行が増えたぶんは上へ伸ばす（2026-09-19のユーザー指定）。戻り値は0以下。
    /// </summary>
    public static float BottomAnchorOffsetMeters(PanelStyle style, float widthMeters, int panelHeight)
    {
        var metersPerPixel = widthMeters / style.Width;

        // 上限との差の半分だけ中心を下げると、下端は上限のときと同じ位置になる。
        return -(style.MaxHeight - panelHeight) / 2f * metersPerPixel;
    }

    /// <summary>スクロールの帯と右端の間隔（px）。</summary>
    private const float ScrollBarMargin = 4f;

    /// <summary>スクロールの帯の上下に取る余白（px）。</summary>
    private const float ScrollBarInset = 4f;

    /// <summary>
    /// つまみの最小の高さ（px）。行が多いほど短くなるが、これより短くはしない
    /// （手首の距離で見失わないため）。
    /// </summary>
    public const float MinThumbHeight = 24f;

    /// <summary>スクロールの溝（パネル内のpx座標）。</summary>
    public static RectangleF ScrollTrackRect(PanelStyle style, int viewportHeight)
        => new(
            style.Width - style.ScrollBarWidth - ScrollBarMargin,
            style.ViewportTop + ScrollBarInset,
            style.ScrollBarWidth,
            MathF.Max(0f, viewportHeight - (ScrollBarInset * 2f)));

    /// <summary>いまのスクロール位置に対応するつまみの矩形（パネル内のpx座標）。</summary>
    public static RectangleF ScrollThumbRect(PanelStyle style, int viewportHeight, float contentHeight, float scrollOffset)
    {
        var track = ScrollTrackRect(style, viewportHeight);
        var content = MathF.Max(contentHeight, viewportHeight);

        var height = Math.Clamp(track.Height * (viewportHeight / content), MinThumbHeight, track.Height);
        var maxOffset = MathF.Max(1f, content - viewportHeight);
        var top = track.Y + ((track.Height - height) * Math.Clamp(scrollOffset / maxOffset, 0f, 1f));

        return new RectangleF(track.X, top, track.Width, height);
    }

    // ------------------------------------------------------------------ 命中の判定（→実装メモ5.32）

    /// <summary>
    /// パネルの中心を原点とした局所座標（m・+Yが上）を、パネル内の画素座標へ直す。
    ///
    /// 右手のレイとパネルの交点から「どの行を指しているか」を求めるのに使う。
    /// 交点のUV（<c>VROverlayIntersectionResults_t.vUVs</c>）は上下の向きが環境依存なので、
    /// UVではなく交点の座標そのものから求める。
    /// </summary>
    /// <param name="panelHeight">いまのパネルの高さ（px）。行が少ないと上限より小さい。</param>
    public static PointF PixelFromPanelLocal(PanelStyle style, float widthMeters, int panelHeight, float localX, float localY)
    {
        if (widthMeters <= 0f)
            return new PointF(float.NaN, float.NaN);

        var pixelsPerMeter = style.Width / widthMeters;

        // テクスチャのy は下向きに増えるので、上下だけ符号を反転する。
        return new PointF(
            (style.Width / 2f) + (localX * pixelsPerMeter),
            (panelHeight / 2f) - (localY * pixelsPerMeter));
    }

    /// <summary>
    /// 指している行を示す白い帯の右端（px）。スクロールの溝の手前で止める。
    /// 溝を出していない（行が表示領域に収まる）ときは、パネルの右端まで伸ばす（右に溝ぶんの空きを残さない→実装メモ5.67）。
    /// </summary>
    public static float RowHighlightRight(PanelStyle style, bool scrollable = true)
        => scrollable ? ScrollTrackRect(style, style.MaxViewportHeight).X - 2f : style.Width;

    /// <summary>
    /// 指している行の帯（パネル内のpx座標）。表示領域からはみ出した部分は切り落とす。
    /// 見えていない行を指定した場合は高さ0の矩形を返す。
    /// </summary>
    /// <param name="rowTop">行の上端（内容座標。帯のぶんは含めない）。</param>
    public static RectangleF RowHighlightRect(
        PanelStyle style,
        int viewportHeight,
        float scrollOffset,
        float rowTop,
        float rowHeight,
        bool scrollable = true)
    {
        var top = MathF.Max(style.ViewportTop + rowTop - scrollOffset, style.ViewportTop);
        var bottom = MathF.Min(style.ViewportTop + rowTop - scrollOffset + rowHeight, style.ViewportTop + viewportHeight);

        return new RectangleF(0f, top, RowHighlightRight(style, scrollable), MathF.Max(0f, bottom - top));
    }

    // ------------------------------------------------------------------ 目印（→実装メモ5.32）

    /// <summary>目印を選ぶポップアップの選択肢1つの一辺（px）。</summary>
    public const float MarkChoiceSize = 88f;

    /// <summary>選択肢どうしの間隔（px）。</summary>
    public const float MarkChoiceGap = 8f;

    /// <summary>ポップアップの縁と選択肢の間（px）。</summary>
    public const float MarkPopupPadding = 12f;

    /// <summary>ポップアップの枠線の太さ（px）。ボタンと同じく、手首の距離で辺が消えない太さにする。</summary>
    public const float MarkPopupBorder = 2f;

    /// <summary>
    /// 目印の選択肢の右に置く「ブラウザで開く」／「ここへ戻る」の幅（px・2026-09-26のユーザー指定→実装メモ5.43・5.53）。
    /// 印と、長いほうの「ブラウザで開く」の文字（22px）が1行に入る幅にする。高さは選択肢と同じ。
    /// </summary>
    public const float ReturnChoiceWidth = 236f;

    /// <summary>目印の選択肢と「ここへ戻る」の間（px）。真ん中に区切りの線を引く。</summary>
    public const float ReturnChoiceGap = 20f;

    /// <summary>
    /// ポップアップの大きさ（px）。目印の選択肢の数で決まり、右端に「ここへ戻る」が1つ付く。
    /// </summary>
    public static SizeF MarkPopupSize(int choices)
        => new(
            (MarkPopupPadding * 2f) + (MarkChoiceSize * choices) + (MarkChoiceGap * Math.Max(0, choices - 1))
                + ReturnChoiceGap + ReturnChoiceWidth,
            (MarkPopupPadding * 2f) + MarkChoiceSize);

    /// <summary>
    /// ポップアップを置く矩形（パネル内のpx座標）。
    /// 指している行の上に、左右は中央・上下はその行の中央へ重ねる。
    /// 端の行でも欠けないよう、表示領域の中へ収める。
    /// </summary>
    public static RectangleF MarkPopupRect(PanelStyle style, int viewportHeight, RectangleF rowRect, int choices)
    {
        var size = MarkPopupSize(choices);
        var x = (rowRect.Right - size.Width) / 2f;

        // 上下の端でも表示領域の縁に貼り付かないよう、わずかに内側で止める。
        const float inset = 6f;

        var top = rowRect.Y + ((rowRect.Height - size.Height) / 2f);
        var min = style.ViewportTop + inset;
        var max = style.ViewportTop + viewportHeight - size.Height - inset;

        return new RectangleF(x, max <= min ? min : Math.Clamp(top, min, max), size.Width, size.Height);
    }

    /// <summary>ポップアップの中の <paramref name="index"/> 番目の選択肢の矩形。</summary>
    public static RectangleF MarkChoiceRect(RectangleF popup, int index)
        => new(
            popup.X + MarkPopupPadding + (index * (MarkChoiceSize + MarkChoiceGap)),
            popup.Y + MarkPopupPadding,
            MarkChoiceSize,
            MarkChoiceSize);

    /// <summary>ポップアップの右端の「ここへ戻る」の矩形（→実装メモ5.43）。</summary>
    public static RectangleF ReturnChoiceRect(RectangleF popup)
        => new(
            popup.Right - MarkPopupPadding - ReturnChoiceWidth,
            popup.Y + MarkPopupPadding,
            ReturnChoiceWidth,
            MarkChoiceSize);

    /// <summary>
    /// ポップアップの中で <paramref name="point"/> が指している項目。目印の選択肢は 0 から順、
    /// 「ここへ戻る」は <see cref="ReturnChoiceIndex"/>、どれでもなければ -1。
    /// VR内の右手のレイとデスクトップのウィンドウのマウスで同じ計算を使う。
    /// </summary>
    public static int PopupItemAt(RectangleF popup, PointF point, int choices)
    {
        if (popup.IsEmpty)
            return -1;

        for (var i = 0; i < choices; i++)
        {
            if (MarkChoiceRect(popup, i).Contains(point))
                return i;
        }

        return ReturnChoiceRect(popup).Contains(point) ? ReturnChoiceIndex : -1;
    }

    /// <summary>ポップアップの「ここへ戻る」を表す番号（目印の選択肢の番号とは重ならない）。</summary>
    public const int ReturnChoiceIndex = 100;

    // ------------------------------------------------------------------ 履歴リセットの確認（→実装メモ5.65）

    /// <summary>確認の枠の高さ（px）。行がないときの最も低いパネル（見出し＋1行＋下部）にも収まる高さにする。</summary>
    public const float ConfirmBoxHeight = 132f;

    /// <summary>確認の枠の左右の余白（パネルの縁から・px）。</summary>
    public const float ConfirmBoxInset = 24f;

    /// <summary>確認の枠の中の余白（px）。</summary>
    public const float ConfirmPadding = 24f;

    /// <summary>確認のボタン1つの大きさ（px）。</summary>
    public static readonly SizeF ConfirmButtonSize = new(170f, 60f);

    /// <summary>確認のボタンの間（px）。</summary>
    public const float ConfirmButtonGap = 16f;

    /// <summary>確認の「キャンセル」を表す番号。</summary>
    public const int ConfirmCancel = 0;

    /// <summary>確認の「リセット」を表す番号。</summary>
    public const int ConfirmAccept = 1;

    /// <summary>
    /// 履歴リセットの確認の枠（パネル内のpx座標）。パネルの左右いっぱい（少し内側）に、縦はパネルの中央へ置く。
    /// 文字は左、ボタンは右に1段で並べて、行がないときの低いパネルにも収める。
    /// </summary>
    public static RectangleF ConfirmBoxRect(PanelStyle style, int panelHeight)
    {
        var height = MathF.Min(ConfirmBoxHeight, panelHeight - 8f);
        return new RectangleF(ConfirmBoxInset, (panelHeight - height) / 2f, style.Width - (ConfirmBoxInset * 2f), height);
    }

    /// <summary>確認のボタンの矩形。右から「リセット」、その左に「キャンセル」。</summary>
    public static RectangleF ConfirmButtonRect(RectangleF box, int which)
    {
        var size = ConfirmButtonSize;
        var right = box.Right - ConfirmPadding - (which == ConfirmCancel ? size.Width + ConfirmButtonGap : 0f);

        return new RectangleF(right - size.Width, box.Y + ((box.Height - size.Height) / 2f), size.Width, size.Height);
    }

    /// <summary>確認の枠の中で <paramref name="point"/> が指しているボタン。どれでもなければ -1。</summary>
    public static int ConfirmItemAt(RectangleF box, PointF point)
    {
        if (box.IsEmpty)
            return -1;

        if (ConfirmButtonRect(box, ConfirmAccept).Contains(point))
            return ConfirmAccept;

        return ConfirmButtonRect(box, ConfirmCancel).Contains(point) ? ConfirmCancel : -1;
    }
}

