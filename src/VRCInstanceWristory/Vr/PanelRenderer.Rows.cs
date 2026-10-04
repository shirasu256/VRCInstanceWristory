using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Scrolling;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 行（→仕様8.1節）。行の下絵へ描くのは内容が変わったときだけで、スクロールは <see cref="Compose"/> がそこから窓を写すだけで行う。
/// 行の高さの実測と、内容座標からの行の割り出しもここに置く（描くときと命中を見るときで同じ計算を使う）。
/// </summary>
public sealed partial class PanelRenderer
{
    /// <summary>各行の高さを実測する。スクロール計算と描画で同じ結果を使う。</summary>
    public List<RowLayout> Measure(IReadOnlyList<DisplayRow> rows)
    {
        var layouts = new List<RowLayout>(rows.Count);
        var available = _style.IdColumnWidth;

        foreach (var row in rows)
        {
            var font = _fonts.Id;
            var width = MeasureWidth(row.InstanceId, font);

            if (width > available)
            {
                font = _fonts.IdSmall;
                width = MeasureWidth(row.InstanceId, font);
            }

            IReadOnlyList<string> lines = width <= available
                ? [row.InstanceId]
                : WrapId(row.InstanceId, font, available);

            // 標準行は3段の付加情報が収まる固定高さ。IDが折り返す例外のときだけ高くする。
            var idHeight = lines.Count * (FontHeight(font) + 2f) + 24f;
            var height = Math.Max(_style.RowHeight, idHeight);

            // 行の上に付ける帯（→実装メモ5.29・5.30）。
            // 高さに含めておけば、スクロール計算も縦幅の見積もりもそのまま合う。
            if (HasBand(row))
                height += _style.AbsenceBandHeight;

            layouts.Add(new RowLayout(row, font, lines, height));
        }

        return layouts;
    }

    public List<ScrollRow> ToScrollRows(IReadOnlyList<RowLayout> layouts)
        => layouts.Select(l => new ScrollRow(l.Row.EventId, l.Height)).ToList();
    /// <summary>指している行（内容座標）。<see cref="Top"/> と <see cref="Height"/> には帯を含めない。</summary>
    public readonly record struct RowHit(int Index, float Top, float Height);

    /// <summary>
    /// <paramref name="index"/> 番目の行の位置（内容座標）。帯のぶんは含めない。
    /// ポップアップを開いたままの行を、毎フレーム置き直すのに使う。
    /// </summary>
    public RowHit RowAt(IReadOnlyList<RowLayout> layouts, int index)
    {
        var y = 0f;

        for (var i = 0; i < index && i < layouts.Count; i++)
            y += layouts[i].Height;

        if (index < 0 || index >= layouts.Count)
            return new RowHit(index, y, 0f);

        var layout = layouts[index];
        var height = layout.Height;

        if (HasBand(layout.Row))
        {
            var band = Math.Min(_style.AbsenceBandHeight, height);
            y += band;
            height -= band;
        }

        return new RowHit(index, y, height);
    }

    /// <summary>
    /// 一覧の内容座標 <paramref name="contentY"/> にある行を探す（2026-09-22のユーザー指定→実装メモ5.32）。
    ///
    /// 行の上に付く帯（「対象外のインスタンスへ移動」・クラッシュ）は行と行の間の断りなので、
    /// そこを指している間はどの行も指していないことにする。
    /// </summary>
    public bool TryHitRow(IReadOnlyList<RowLayout> layouts, float contentY, out RowHit hit)
    {
        hit = default;
        var y = 0f;

        for (var i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            var top = y;
            var height = layout.Height;

            y += layout.Height;

            if (HasBand(layout.Row))
            {
                var band = Math.Min(_style.AbsenceBandHeight, height);
                top += band;
                height -= band;
            }

            if (contentY < top || contentY >= top + height)
                continue;

            hit = new RowHit(i, top, height);
            return true;
        }

        return false;
    }

    /// <summary>
    /// この行の上に帯を入れるか。クラッシュと「対象外のインスタンスへ移動」が重なっても、
    /// 帯は1枚にまとめて中身を並べる（2026-09-21のユーザー指定→実装メモ5.30）。
    /// </summary>
    private static bool HasBand(DisplayRow row)
        => row.CrashedBefore || row.ExcludedBefore;
    /// <summary>
    /// 行だけを下絵へ描く。戻り値は下絵の高さ（px）。
    /// 表示領域より短い内容でも、上詰めで見せるため最低でも表示領域ぶん確保する。
    ///
    /// 行の内容が変わったときだけ呼べばよい。スクロールは <see cref="Compose"/> が
    /// この下絵から窓を写すだけで行う。
    /// </summary>
    public int RenderRows(IReadOnlyList<RowLayout> layouts)
    {
        var content = layouts.Sum(l => l.Height);
        var height = PanelGeometry.RowsTextureHeight(content, ViewportHeight);

        EnsureRowsSurface(height);
        ForgetStaleBrushes();

        _rowCount = layouts.Count;
        _contentHeight = content;
        _since = layouts.Count > 0 ? layouts[0].Row.JoinText : null;
        _sinceWithDate = layouts.Count > 0 && layouts[0].Row.JoinDateText.Length > 0 ? layouts[0].Row.JoinDateText : _since;

        var graphics = _rowsGraphics!;
        graphics.Clear(Color.Transparent);

        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.FillRectangle(BrushFor(Color.FromArgb(_style.BackgroundAlpha, _style.Surface)), 0, 0, Width, height);

        graphics.CompositingMode = CompositingMode.SourceOver;
        DrawRows(graphics, layouts, scrollOffset: 0f, top: 0f, clipHeight: height);

        return height;
    }

    private void EnsureRowsSurface(int height)
    {
        if (_rowsBitmap is not null && _rowsBitmap.Height == height && _rowsBitmap.Width == Width)
            return;

        _rowsGraphics?.Dispose();
        _rowsBitmap?.Dispose();

        _rowsBitmap = new Bitmap(Width, height, PixelFormat.Format32bppArgb);
        _rowsGraphics = Graphics.FromImage(_rowsBitmap);
        _rowsGraphics.SmoothingMode = SmoothingMode.None;
        _rowsGraphics.PixelOffsetMode = PixelOffsetMode.Half;
        _rowsGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        _rowsGraphics.CompositingQuality = CompositingQuality.HighQuality;
        _rowsGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }
    private void DrawRows(
        Graphics graphics,
        IReadOnlyList<RowLayout> layouts,
        float scrollOffset,
        float? top = null,
        int? clipHeight = null)
    {
        var clipTop = top ?? _style.ViewportTop;
        var clip = new Rectangle(0, (int)clipTop, Width, clipHeight ?? ViewportHeight);
        graphics.SetClip(clip);

        var text = BrushFor(_style.Text);
        var muted = BrushFor(_style.Muted);
        var accent = BrushFor(_style.Accent);
        var currentBrush = BrushFor(Color.FromArgb(_style.BackgroundAlpha, _style.CurrentRow));
        var ruleBrush = BrushFor(Color.FromArgb(_style.BackgroundAlpha, _style.Rule));

        var y = clipTop - scrollOffset;
        var columns = FirstLineColumnsFor(layouts);

        for (var i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            var height = layout.Height;

            // 次が「対象外のインスタンスへ移動」の帯か現在地の行なら、その境目は行の区切りではなく
            // ひとまとまりの上端なので、線を端まで伸ばす（2026-09-21のユーザー指定）。
            var next = i + 1 < layouts.Count ? layouts[i + 1].Row : null;
            var fullWidthRule = next is not null && (HasBand(next) || next.IsCurrent);

            if (y + height >= clip.Top && y <= clip.Bottom)
                DrawRow(graphics, layout, y, height, columns, text, muted, accent, currentBrush, ruleBrush, fullWidthRule);

            y += height;
        }

        graphics.ResetClip();
    }

    /// <summary>1段目の区切りの間隔（時刻・N分前・回数・人数のあいだ）。</summary>
    private const float FirstLineGap = 14f;

    /// <summary>
    /// 1段目の各欄の左端（パネル内のpx座標）。
    /// 並びは `00:35 - 00:36 (9分前) 1回目 24人`（2026-09-25のユーザー指定→実装メモ5.36）。
    /// </summary>
    public readonly record struct FirstLineColumns(float Time, float Ago, float Ordinal, float People);

    /// <summary>
    /// 1段目の欄の位置を、一覧の全行で共通に決める。描くときと検証で同じ計算を使う。
    ///
    /// 「N分前」と回数は行ごとに文字数が変わるので、欄の幅はこの一覧でいちばん長い値に合わせ、
    /// どの行でも回数と人数の左端がそろうようにする（表の列と同じ考え方）。
    /// 「N分前」は2桁ぶんを下限にして、9分前から10分前へ変わるたびに後ろの欄が動かないようにする。
    /// 時刻は等幅なので、「00:00 - 00:00」の幅で足りる（滞在中の行は短いが、同じ幅を取る）。
    /// </summary>
    public FirstLineColumns FirstLineColumnsFor(IReadOnlyList<RowLayout> layouts)
    {
        var time = MeasureWidth(RowFormatter.TimeSample, _fonts.AuxMono);
        var ago = MeasureWidth(RowFormatter.AgoSample, _fonts.Aux);
        var ordinal = 0f;

        foreach (var layout in layouts)
        {
            time = MathF.Max(time, MeasureWidth(layout.Row.TimeText, _fonts.AuxMono));
            ago = MathF.Max(ago, MeasureWidth(layout.Row.AgoText, _fonts.Aux));
            ordinal = MathF.Max(ordinal, MeasureWidth(layout.Row.OrdinalText, _fonts.Aux));
        }

        var left = (float)_style.AuxColumnLeft;
        var agoLeft = left + time + FirstLineGap;
        var ordinalLeft = agoLeft + ago + FirstLineGap;

        return new FirstLineColumns(left, agoLeft, ordinalLeft, ordinalLeft + ordinal + FirstLineGap);
    }

    private void DrawRow(
        Graphics graphics,
        RowLayout layout,
        float top,
        float height,
        FirstLineColumns columns,
        Brush text,
        Brush muted,
        Brush accent,
        Brush currentBrush,
        Brush ruleBrush,
        bool fullWidthRule)
    {
        var row = layout.Row;

        // 行の上に帯を入れる（2026-09-21のユーザー指定）。クラッシュと「対象外のインスタンスへ移動」が
        // 重なっても1枚にまとめ、中身を並べて出す。
        // 帯のぶんは行の中身から外し、以降は帯の下だけを1行として扱う。
        if (HasBand(row))
        {
            var bandHeight = Math.Min(_style.AbsenceBandHeight, height);
            DrawBand(graphics, row, top, bandHeight, ruleBrush);

            top += bandHeight;
            height -= bandHeight;
        }

        if (row.IsCurrent)
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.FillRectangle(currentBrush, 0, top, Width, height);
            graphics.CompositingMode = CompositingMode.SourceOver;
        }

        DrawRule(graphics, ruleBrush, top + height - 1, fullWidthRule);

        var idBrush = row.IsCurrent ? accent : text;
        var idLineHeight = FontHeight(layout.IdFont) + 2f;
        var idBlockHeight = layout.IdLines.Count * idLineHeight;
        var idY = top + (height - idBlockHeight) / 2f;

        if (row.IsCurrent)
        {
            // 現在地の行は ▶ だけを出す（2026-09-21のユーザー指定）。
            // いま滞在している行だと分かれば足り、棒と重ねると形が読み取りにくくなる。
            // 棒と左右の中心を揃えるため、印の列の横中央へ置く。
            var markerY = top + (height - FontHeight(_fonts.Marker)) / 2f;
            var markerX = _style.MarkerColumnCenter - (MeasureWidth(MarkerGlyph, _fonts.Marker) / 2f);
            graphics.DrawString(MarkerGlyph, _fonts.Marker, accent, markerX, markerY, _format);
        }
        else
        {
            // 滞在時間の棒は ▶ と同じ列・同じ中心に置く。
            DrawStayBar(graphics, row.StayFraction, top, height);
        }

        foreach (var line in layout.IdLines)
        {
            graphics.DrawString(line, layout.IdFont, idBrush, _style.IdColumnLeft, idY, _format);
            idY += idLineHeight;
        }

        // インスタンス番号の後ろに付けた目印（2026-09-22のユーザー指定→実装メモ5.32）。
        DrawMark(graphics, layout, top, height);

        // 右列: 1段目 時刻・N分前・回数・人数、2段目 ワールド名、3段目 種類・短縮Group ID。
        var auxLeft = _style.AuxColumnLeft;
        var auxWidth = _style.AuxColumnWidth;
        var block = _style.AuxLineHeight * 3f;
        var auxY = top + (height - block) / 2f;

        // 時刻欄は「入室 - 退出」（2026-09-20のユーザー指定）。
        graphics.DrawString(row.TimeText, _fonts.AuxMono, row.IsCurrent ? accent : text, columns.Time, auxY, _format);

        // 時刻（等幅）の後ろの文字は日本語のフォントなので、上端ではなくベースラインを時刻にそろえる
        // （上端でそろえると字面が上下にずれて見える→実装メモ5.37）。
        var auxTextY = auxY + Ascent(_fonts.AuxMono) - Ascent(_fonts.Aux);

        // 退出してからの分数は時刻の後ろ（2026-09-25のユーザー指定）。滞在中の行は空欄になる。
        if (row.AgoText.Length > 0)
            graphics.DrawString(row.AgoText, _fonts.Aux, muted, columns.Ago, auxTextY, _format);

        graphics.DrawString(row.OrdinalText, _fonts.Aux, muted, columns.Ordinal, auxTextY, _format);

        // 退出時にいた人数は回数の後ろ（2026-09-20のユーザー指定）。
        if (row.PeopleText.Length > 0)
            graphics.DrawString(row.PeopleText, _fonts.Aux, muted, columns.People, auxTextY, _format);

        // 滞在中に撮った写真の印と枚数は、1段目の右端にそろえる（2026-09-26のユーザー指定→実装メモ5.47）。
        if (row.PhotoCount > 0)
            DrawPhotoCount(graphics, row.PhotoCount, auxLeft + auxWidth, auxY, auxTextY, muted);

        auxY += _style.AuxLineHeight;
        DrawFadingText(graphics, row.WorldName, _fonts.Aux, _style.Text, auxLeft, auxY, auxWidth);

        auxY += _style.AuxLineHeight;
        DrawFadingText(graphics, row.TypeText, _fonts.Aux, _style.Muted, auxLeft, auxY, auxWidth);
    }

    /// <summary>写真の印の一辺（px）。付加情報の文字（22px）と同じくらいの大きさにする。</summary>
    public const float PhotoIconSize = 22f;

    /// <summary>写真の印と枚数の間（px）。</summary>
    private const float PhotoCountGap = 5f;

    /// <summary>
    /// 写真の印（カメラ）と枚数（→実装メモ5.47）。<paramref name="right"/> に右端をそろえて、1段目の縦中央に置く。
    /// 色は回数・人数と同じ <see cref="PanelStyle.Muted"/>。
    /// </summary>
    private void DrawPhotoCount(Graphics graphics, int count, float right, float lineTop, float textY, Brush muted)
    {
        var text = count.ToString(CultureInfo.InvariantCulture);
        var width = MeasureWidth(text, _fonts.Aux);
        var textX = right - width;
        var icon = new RectangleF(
            textX - PhotoCountGap - PhotoIconSize,
            lineTop + ((_style.AuxLineHeight - PhotoIconSize) / 2f),
            PhotoIconSize,
            PhotoIconSize);

        IconPainter.DrawCamera(graphics, icon, _style.Muted);
        graphics.DrawString(text, _fonts.Aux, muted, textX, textY, _format);
    }

    /// <summary>
    /// 写真の印と枚数を置く矩形（行の中の座標・検証用）。<paramref name="count"/> が0なら空。
    /// </summary>
    public RectangleF PhotoCountRect(int count, float rowTop, float rowHeight)
    {
        if (count <= 0)
            return RectangleF.Empty;

        var block = _style.AuxLineHeight * 3f;
        var lineTop = rowTop + ((rowHeight - block) / 2f);
        var width = MeasureWidth(count.ToString(CultureInfo.InvariantCulture), _fonts.Aux) + PhotoCountGap + PhotoIconSize;
        var right = _style.AuxColumnLeft + _style.AuxColumnWidth;

        return new RectangleF(right - width, lineTop, width, _style.AuxLineHeight);
    }

    /// <summary>
    /// インスタンス番号の後ろに出す目印（2026-09-22のユーザー指定→実装メモ5.32）。
    ///
    /// IDの最後の行の末尾から <see cref="PanelStyle.MarkGap"/> だけ空けて、その行の中央へ置く。
    /// IDが長くてID列に収まらないときは、付加情報の列に食い込まないよう列の右端で止める。
    /// </summary>
    private void DrawMark(Graphics graphics, RowLayout layout, float top, float height)
    {
        var mark = layout.Row.Mark;

        if (mark == Core.Marks.InstanceMark.None)
            return;

        var size = _style.MarkSize;
        var lineHeight = FontHeight(layout.IdFont) + 2f;
        var blockHeight = layout.IdLines.Count * lineHeight;

        // 最後の行の縦中央に合わせる（折り返した行は下の段の横に付く）。
        var lastLineTop = top + ((height - blockHeight) / 2f) + ((layout.IdLines.Count - 1) * lineHeight);
        var y = lastLineTop + ((lineHeight - size) / 2f);

        var idWidth = MeasureWidth(layout.IdLines[^1], layout.IdFont);
        var x = MathF.Min(
            _style.IdColumnLeft + idWidth + _style.MarkGap,
            _style.IdColumnLeft + _style.IdColumnWidth - size);

        MarkPainter.Draw(graphics, mark, new RectangleF(x, y, size, size), _style.MarkColor(mark));
    }

    /// <summary>
    /// 行の区切りの線（1px）。
    ///
    /// ふだんは左右の余白のぶんだけ短くして、行が続いていることを示す。
    /// <paramref name="fullWidth"/> のときはテクスチャの端まで伸ばし、
    /// 「対象外のインスタンスへ移動」の帯と現在地の行を、前の行から切り離して見せる
    /// （2026-09-21のユーザー指定）。
    ///
    /// 1px幅のペンは線の中心が座標に乗るため、端では半分が外へ出て消える。
    /// 塗りつぶしの矩形で描けば、指定した1行ぶんがそのまま残る（→実装メモ5.18）。
    /// </summary>
    private void DrawRule(Graphics graphics, Brush ruleBrush, float y, bool fullWidth)
    {
        if (fullWidth)
        {
            graphics.FillRectangle(ruleBrush, 0f, y, Width, 1f);
            return;
        }

        graphics.FillRectangle(
            ruleBrush,
            _style.PaddingLeft,
            y,
            Width - _style.PaddingRight - _style.PaddingLeft,
            1f);
    }

    /// <summary>
    /// 行と行の間に入れる帯（2026-09-21のユーザー指定）。
    ///
    /// 「対象外のインスタンスへ移動」（→実装メモ5.29・5.38）と
    /// 「VRChat クライアントクラッシュ」（→実装メモ5.30）の両方に使う。
    /// 一覧は入室の時刻順に詰めて並ぶので、帯がないと「前の行の続き」に見えてしまう。
    /// 両方が立つ区間でも帯は1枚で、中身を `・` で並べる。
    ///
    /// 文字は <see cref="PanelStyle.Muted"/> で描き、クラッシュの断りの部分だけを
    /// <see cref="PanelStyle.Crash"/>（赤）にする。中央寄せは並べた全体の幅で決めるので、
    /// 色を分けても位置は変わらない。
    ///
    /// 縦幅はインスタンスの行の1/3、背景は行よりわずかに明るくする。
    /// 背景は行と同じく置き換えで塗る（重ねると不透明度が設定値より高くなるため）。
    /// 上下は端まで伸ばした線で挟み、行の一部ではないことを示す。
    /// </summary>
    private void DrawBand(Graphics graphics, DisplayRow row, float top, float height, Brush ruleBrush)
    {
        if (height <= 0f)
            return;

        var mode = graphics.CompositingMode;
        graphics.CompositingMode = CompositingMode.SourceCopy;

        graphics.FillRectangle(BrushFor(Color.FromArgb(_style.BackgroundAlpha, _style.AbsenceBand)), 0, top, Width, height);

        graphics.CompositingMode = mode;

        var font = _fonts.Absence;
        var segments = RowFormatter.BandSegments(row.CrashedBefore, row.ExcludedBefore, row.ExcludedBeforeCrash);

        var total = 0f;
        foreach (var segment in segments)
            total += MeasureWidth(segment.Text, font);

        // 左右の中央。右端はスクロールの溝のぶんだけ内側にあるので、その間で中央に置く。
        var left = _style.PaddingLeft;
        var right = Width - _style.PaddingRight - _style.ScrollBarWidth;
        var x = left + ((right - left - total) / 2f);
        var y = top + ((height - FontHeight(font)) / 2f);

        var muted = BrushFor(_style.Muted);
        var crash = BrushFor(_style.Crash);

        foreach (var segment in segments)
        {
            graphics.DrawString(segment.Text, font, segment.Crash ? crash : muted, x, y, _format);
            x += MeasureWidth(segment.Text, font);
        }

        // 帯の下側の線。上側は前の行の区切り（端まで伸ばしたもの）が兼ねる。
        DrawRule(graphics, ruleBrush, top + height - 1f, fullWidth: true);
    }

    /// <summary>現在地の印。滞在時間の棒と左右の中心を揃えて、印の列の横中央へ置く。</summary>
    public const string MarkerGlyph = "▶";

    /// <summary>
    /// 印の列に出す滞在時間の棒（2026-09-21のユーザー指定）。
    /// どのインスタンスに長くいたかを、時刻を読まずに見分けるための印。
    /// 現在地の行には出さない（そこは ▶ が入る）。
    ///
    /// 細い縦棒で、両端は半円。行の中央から上下へ伸び、上下には余白を残す。
    /// 長さは滞在時間に応じ、保持時間（既定60分）で最大に達する。
    /// 濃さは ▶ の1/3で、現在地の印より控えめに見えるようにする。
    ///
    /// 半円の縁だけはアンチエイリアスを使う。背景の矩形と違い、ここは面の縁が
    /// 背景の不透明度に影響しないので、滑らかにしてよい。
    /// </summary>
    private void DrawStayBar(Graphics graphics, float fraction, float top, float height)
    {
        var width = _style.StayBarWidth;
        var length = _style.StayBarLength(fraction, height);

        if (width <= 0f || length <= 0f)
            return;

        var x = _style.StayBarLeft;
        var y = top + ((height - length) / 2f);

        using var path = new GraphicsPath();

        // 上下の半円をつないだ長丸。長さが太さと等しいときはそのまま円になる。
        path.AddArc(x, y, width, width, 180f, 180f);
        path.AddArc(x, y + length - width, width, width, 0f, 180f);
        path.CloseFigure();

        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        graphics.FillPath(BrushFor(Color.FromArgb(_style.StayBarAlpha, _style.Accent)), path);

        graphics.SmoothingMode = smoothing;
    }

    /// <summary>右端のフェードの幅（px）。この区間で不透明から透明へ変わる。</summary>
    private const float FadeWidth = 56f;

    /// <summary>
    /// ワールド名やGroup IDなど、省略記号で削りたくない文字列をフルの内容のまま描く。
    /// 収まりきらない場合は末尾を切るのではなく、右端（収まる境界）に近づくほど
    /// 徐々に透明にして自然にフェードアウトさせる（3段目は2026-09-17、
    /// ワールド名の2段目は2026-09-20のユーザー指定）。
    ///
    /// 透明度は文字単位ではなく<b>画素単位</b>で変える（2026-09-18のユーザー指定）。
    /// 1文字に1つのアルファ値を与えると、文字の幅ぶんずつ段になって見えるため、
    /// 横方向のグラデーションブラシで文字を描いて滑らかに変化させる。
    /// </summary>
    private void DrawFadingText(Graphics graphics, string text, Font font, Color color, float x, float y, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            return;

        if (MeasureWidth(text, font) <= maxWidth)
        {
            graphics.DrawString(text, font, BrushFor(color), x, y, _format);
            return;
        }

        var fadeStart = Math.Max(0f, maxWidth - FadeWidth);
        var height = font.GetHeight(graphics) + 2f;

        // 右端の外へ文字がはみ出すと、グラデーションの外側で繰り返されて再び不透明になる。
        // 先に切り取っておく。呼び出し側の切り取り範囲は Save/Restore で保つ。
        var state = graphics.Save();
        graphics.SetClip(new RectangleF(x, y, maxWidth, height), CombineMode.Intersect);

        // 縁の1画素がにじまないよう、グラデーションの矩形は描画範囲より1pxずつ広く取る。
        var area = new RectangleF(x - 1f, y - 1f, maxWidth + 2f, height + 2f);
        var transparent = Color.FromArgb(0, color);

        using (var brush = new LinearGradientBrush(area, color, transparent, LinearGradientMode.Horizontal))
        {
            // fadeStart までは不透明のまま、そこから右端へ向けて透明にする。
            brush.InterpolationColors = new ColorBlend
            {
                Colors = [color, color, transparent],
                Positions = [0f, (fadeStart + 1f) / area.Width, 1f],
            };

            graphics.DrawString(text, font, brush, x, y, _format);
        }

        graphics.Restore(state);
    }

    /// <summary>IDは省略しないので、収まらない場合だけ左列内で折り返す。</summary>
    private List<string> WrapId(string id, Font font, float maxWidth)
    {
        var lines = new List<string>();
        var start = 0;

        while (start < id.Length)
        {
            var length = 1;
            while (start + length < id.Length && MeasureWidth(id.Substring(start, length + 1), font) <= maxWidth)
                length++;

            lines.Add(id.Substring(start, length));
            start += length;
        }

        return lines;
    }
}
