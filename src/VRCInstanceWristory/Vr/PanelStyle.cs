using System.Drawing;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// パネルの寸法と配色（仕様8.1節の調整開始値）。
/// 960px幅、行高112px、履歴領域は最大840px（112×7.5）、見出しと下部案内で112px。
/// 高さは内容によって変わるので、上限（<see cref="MaxHeight"/>）と
/// 実際の高さ（<see cref="ViewportHeightFor"/> から求める）を区別する。
/// HTMLのピクセル寸法はHMD内の実寸・可読性を保証しないので、実機で調整する。
/// </summary>
public sealed class PanelStyle
{
    public int Width { get; set; } = 960;

    public int HeaderHeight { get; set; } = 64;

    public int FooterHeight { get; set; } = 48;

    public int RowHeight { get; set; } = 112;

    /// <summary>
    /// 表示領域に入れる行数の上限。7.5にして8行目を半分だけ見せ、
    /// 「まだ下に続いている」ことが一目で分かるようにする（2026-09-13のユーザー指定）。
    /// 行がこれより少ないときは、その行数ぶんまで縦幅を詰める（2026-09-19のユーザー指定→<see cref="ViewportHeightFor"/>）。
    /// </summary>
    public float VisibleRows { get; set; } = 7.5f;

    /// <summary>左端から現在地の印までの余白。</summary>
    public int PaddingLeft { get; set; } = 16;

    /// <summary>現在地の印（▶）の幅。</summary>
    public int MarkerWidth { get; set; } = 34;

    /// <summary>
    /// 滞在時間の棒の太さ（px）。印の列の横中央へ、▶ と同じ位置に置く（2026-09-21のユーザー指定）。
    /// 端は半円にするので、滞在0分の行はこの直径の丸になる。
    /// </summary>
    public float StayBarWidth { get; set; } = 8f;

    /// <summary>棒の上下に残す余白（px）。行の高さからこの2倍を引いたものが最大の長さ。</summary>
    public float StayBarPadding { get; set; } = 16f;

    /// <summary>
    /// 棒の不透明度。初めは ▶ の1/3にしたが、実際の描画を見てさらに半分へ下げた
    /// （0.33 → 0.165・2026-09-21のユーザー指定）。行の内容を邪魔しない濃さにする。
    /// </summary>
    public float StayBarOpacity { get; set; } = 0.165f;

    /// <summary>
    /// 滞在時間の割合（0〜1）を長さの割合へ写すときの指数。
    /// 1.0 なら時間に比例。1未満にすると短い滞在ほど伸び、数分の滞在どうしの差が読み取りやすくなる。
    ///
    /// 既定は0.5（＝平方根・2026-09-25のユーザー指定→実装メモ5.36）。比例だと1〜5分の滞在は
    /// 9〜14pxの丸に近い棒にしかならず見分けが付かなかったが、平方根なら17〜29pxに散らばる。
    /// 60分で最大（80px）になるのは比例のときと同じ。
    /// </summary>
    public float StayBarGamma { get; set; } = 0.5f;

    /// <summary>IDの列幅（仕様8.1節の約320px）。</summary>
    public int IdColumnWidth { get; set; } = 320;

    /// <summary>
    /// IDの後ろに置く目印の一辺（px・2026-09-22のユーザー指定→5.32節）。
    /// IDの文字（44px）より一回り小さくして、番号の読み取りを邪魔しない大きさにする。
    /// </summary>
    public float MarkSize { get; set; } = 30f;

    /// <summary>IDの末尾と目印の間隔（px）。</summary>
    public float MarkGap { get; set; } = 12f;

    /// <summary>ID列と付加情報列の間隔。</summary>
    public int ColumnGap { get; set; } = 16;

    public int PaddingRight { get; set; } = 20;

    public float IdFontSize { get; set; } = 44f;

    public float AuxFontSize { get; set; } = 22f;

    public float TitleFontSize { get; set; } = 24f;

    /// <summary>
    /// 見出し右のカウントダウンの文字の大きさ。付加情報よりわずかに大きくして、
    /// 手首の距離でも残り時間が読み取れるようにする（2026-09-19のユーザー指定）。
    /// </summary>
    public float CountdownFontSize { get; set; } = 25f;

    /// <summary>
    /// 見出しのボタン（「延長」「リセット」）の文字の大きさ。付加情報よりわずかに小さくして、
    /// 見出しの中でカウントダウンより控えめに見えるようにする（2026-09-19のユーザー指定）。
    /// </summary>
    public float ResetButtonFontSize { get; set; } = 20f;

    /// <summary>
    /// 行の間に入れる「∧ 対象外のインスタンスへ移動 ∨」の文字の大きさ。
    /// 帯の縦幅が行の1/3しかないので、付加情報より一回り小さくする（2026-09-21のユーザー指定）。
    /// </summary>
    public float AbsenceFontSize { get; set; } = 20f;

    /// <summary>付加情報3段の行送り。</summary>
    public float AuxLineHeight { get; set; } = 30f;

    /// <summary>背景の不透明度。文字には適用しない。</summary>
    public float BackgroundOpacity { get; set; } = 0.9f;

    public int ScrollBarWidth { get; set; } = 6;

    public Color Surface { get; set; } = Color.FromArgb(0x13, 0x1b, 0x22);

    public Color Header { get; set; } = Color.FromArgb(0x18, 0x23, 0x2c);

    public Color Text { get; set; } = Color.FromArgb(0xf0, 0xf6, 0xfa);

    public Color Muted { get; set; } = Color.FromArgb(0xa6, 0xba, 0xc8);

    public Color Rule { get; set; } = Color.FromArgb(0x2d, 0x3b, 0x45);

    public Color Accent { get; set; } = Color.FromArgb(0x73, 0xdc, 0xde);

    public Color CurrentRow { get; set; } = Color.FromArgb(0x1b, 0x36, 0x3d);

    public Color ScrollTrack { get; set; } = Color.FromArgb(0x52, 0x69, 0x75);

    /// <summary>
    /// 「対象外のインスタンスへ移動」の帯の背景（2026-09-21のユーザー指定）。
    /// インスタンスの行（<see cref="Surface"/>）よりわずかに明るくして、行の一部ではなく
    /// 行と行の間の断りだと分かるようにする。各成分を10ずつ上げてある。
    /// </summary>
    public Color AbsenceBand { get; set; } = Color.FromArgb(0x1d, 0x25, 0x2c);

    /// <summary>
    /// 帯の中の「VRChat クライアントクラッシュ」の色（2026-09-21のユーザー指定→5.30節）。
    /// 同じ帯に並ぶ「対象外のインスタンスへ移動」や挟みの記号（<see cref="Muted"/>）と色で区別し、
    /// 異常な終わり方だったことが一目で分かるようにする。暗い背景でも読めるよう、赤は明るめにする。
    /// </summary>
    public Color Crash { get; set; } = Color.FromArgb(0xff, 0x6b, 0x6b);

    /// <summary>
    /// 見出しの「履歴リセット」のボタンの色。クラッシュの赤（<see cref="Crash"/>）に白を12%混ぜて、わずかに白みを増した
    /// （2026-09-29のユーザー指定→実装メモ5.91）。
    /// </summary>
    public Color ClearButton { get; set; } = Color.FromArgb(0xff, 0x7d, 0x7d);

    /// <summary>
    /// 指している行を示す色（2026-09-22のユーザー指定→5.32節）。
    /// 行の上に薄く重ねるだけで、下の文字は読めるままにする。
    /// </summary>
    public Color RowHighlight { get; set; } = Color.FromArgb(0xff, 0xff, 0xff);

    /// <summary>
    /// 指している行に重ねる白の不透明度。「わずかに白くなる」程度に留める
    /// （2026-09-22のユーザー指定）。
    /// </summary>
    public float RowHighlightOpacity { get; set; } = 0.07f;

    /// <summary>
    /// 目印「ハート」の色。クラッシュの赤（<see cref="Crash"/>）と同じ明るさの系統で、
    /// 暗い背景でも沈まない桃色にする（2026-09-22のユーザー指定→5.32節）。
    /// </summary>
    public Color MarkHeart { get; set; } = Color.FromArgb(0xff, 0x8f, 0xa8);

    /// <summary>目印「チェック」の色。</summary>
    public Color MarkCheck { get; set; } = Color.FromArgb(0x7a, 0xdc, 0xa0);

    /// <summary>目印「注意」の色。</summary>
    public Color MarkWarning { get; set; } = Color.FromArgb(0xff, 0xc4, 0x6b);

    /// <summary>完全に見える標準行の数。</summary>
    public int FullyVisibleRows => (int)MathF.Floor(VisibleRows);

    /// <summary>履歴の表示領域の高さの上限（<see cref="VisibleRows"/> 行ぶん）。</summary>
    public int MaxViewportHeight => (int)MathF.Round(RowHeight * VisibleRows);

    /// <summary>テクスチャの高さの上限。見出し・表示領域の上限・下部案内の合計。</summary>
    public int MaxHeight => HeightFor(MaxViewportHeight);

    /// <summary>
    /// 行の合計がこの高さのとき、実際に取る表示領域の高さ。
    /// 上限までは内容ぶんだけにして縦幅を節約し、超えたら上限で頭打ちにする（2026-09-19のユーザー指定）。
    /// 行が1つもなくてもテクスチャが0pxにならないよう、下限は標準行1行ぶんとする。
    /// </summary>
    public int ViewportHeightFor(float contentHeight)
        => Math.Clamp((int)MathF.Ceiling(contentHeight), RowHeight, MaxViewportHeight);

    /// <summary>
    /// 行の間に入れる帯（「対象外のインスタンスへ移動」とクラッシュ）の高さ（px）。
    /// インスタンスの行の1/3とする（2026-09-21のユーザー指定）。
    /// 端数で文字の縁がぼけないよう、整数に丸めて使う。
    /// </summary>
    public int AbsenceBandHeight => (int)MathF.Round(RowHeight / 3f);

    /// <summary>表示領域の高さから求めるテクスチャの高さ。</summary>
    public int HeightFor(int viewportHeight) => HeaderHeight + viewportHeight + FooterHeight;

    public int ViewportTop => HeaderHeight;

    public int IdColumnLeft => PaddingLeft + MarkerWidth;

    public int AuxColumnLeft => IdColumnLeft + IdColumnWidth + ColumnGap;

    public int AuxColumnWidth => Width - AuxColumnLeft - PaddingRight - ScrollBarWidth;

    public byte BackgroundAlpha => (byte)Math.Clamp((int)MathF.Round(BackgroundOpacity * 255f), 0, 255);

    public byte StayBarAlpha => (byte)Math.Clamp((int)MathF.Round(StayBarOpacity * 255f), 0, 255);

    public byte RowHighlightAlpha => (byte)Math.Clamp((int)MathF.Round(RowHighlightOpacity * 255f), 0, 255);

    /// <summary>目印の色。<see cref="Core.Marks.InstanceMark.None"/> には <see cref="Muted"/> を返す。</summary>
    public Color MarkColor(Core.Marks.InstanceMark mark) => mark switch
    {
        Core.Marks.InstanceMark.Heart => MarkHeart,
        Core.Marks.InstanceMark.Check => MarkCheck,
        Core.Marks.InstanceMark.Warning => MarkWarning,
        _ => Muted,
    };

    /// <summary>
    /// 滞在時間の割合（0〜1。<see cref="Core.Presentation.DisplayRow.StayFraction"/>）から求める棒の長さ（px）。
    /// 最小は太さぶん（＝丸）、最大はその行の高さから上下の余白を引いたもの。
    /// その間は割合の平方根で伸ばす（<see cref="StayBarGamma"/>）。
    /// 行はIDが折り返すと標準より高くなるので、最大は行ごとに取る。
    /// </summary>
    public float StayBarLength(float fraction, float rowHeight)
    {
        var max = MathF.Max(StayBarWidth, rowHeight - (StayBarPadding * 2f));
        var t = Math.Clamp(fraction, 0f, 1f);
        var curved = StayBarGamma == 1f ? t : MathF.Pow(t, StayBarGamma);

        return StayBarWidth + ((max - StayBarWidth) * curved);
    }

    /// <summary>印の列の横中央（px）。▶ はこの位置を中心に置く。</summary>
    public float MarkerColumnCenter => PaddingLeft + (MarkerWidth / 2f);

    /// <summary>
    /// 棒の中心を列の中央からずらす量（px・負なら左）。
    ///
    /// ▶ は右向きの三角形で、字面の幅の真ん中に置いても**見た目の中心は左寄り**になる。
    /// 実際の描画を測ると、字面は 22〜42px に広がるのに対し、濃さで重みを付けた中心は
    /// 29.3px で、列の中央（33px）より3.7px 左にある。棒だけを列の中央に置くと、
    /// 現在地の行とそれ以外で印の位置がずれて見える（2026-09-21のユーザー指摘）。
    ///
    /// そこで棒を左へ寄せて、▶ の見た目の中心に合わせる。
    /// 端数にすると縁がぼけるので、画素の境目に乗る量にしてある。
    /// </summary>
    public float StayBarCenterAdjust { get; set; } = -3f;

    /// <summary>棒の左端（px）。▶ の見た目の中心に合わせる。</summary>
    public float StayBarLeft => MarkerColumnCenter + StayBarCenterAdjust - (StayBarWidth / 2f);

    /// <summary>同じ値を持つ別の配色（受け取った側が寸法や不透明度を変えても、渡した側に響かないようにする）。</summary>
    public PanelStyle Copy() => (PanelStyle)MemberwiseClone();
}
