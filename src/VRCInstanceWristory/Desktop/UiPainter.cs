using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// デスクトップのウィンドウとSteamVRのダッシュボードで共通に使う、GDI+の部品の描き方（→実装メモ5.39・5.40）。
/// 字の大きさは論理px（96dpi）で決め、<see cref="Scale"/> 倍して描く。配色はパネルと同じ <see cref="PanelStyle"/>。
///
/// 描くたびに作ると重いもの（単色の筆・折り返した行・縮めた字）は、ここで持っておいて使い回す。
/// 持っているものは倍率ごとで、倍率が変わったら描き手ごと作り直す（<see cref="Dispose"/> でまとめて手放す）。
/// 1つの描き手は1つのスレッドからだけ使う。
/// </summary>
public sealed class UiPainter : IDisposable
{
    private const float BorderWidth = 1f;

    /// <summary>目立たせる項目の値の字の大きさ（論理px）。</summary>
    private const float ProminentValueSize = 22.8f * 1.15f;

    /// <summary>右端のフェードの幅（論理px）。パネルの 56px（960px幅のうち）をウィンドウの字の大きさへ縮めたもの。</summary>
    private const float FadeWidth = 36f;

    /// <summary>折り返した行を持っておく上限（超えたら捨てて作り直す）。</summary>
    private const int MaxWrapCache = 256;

    private readonly Bitmap _measureSurface;
    private readonly Graphics _measure;
    private readonly Dictionary<int, SolidBrush> _brushes = [];
    private readonly Dictionary<(string Text, Font Font, float Width, int MaxLines), IReadOnlyList<string>> _wrapped = [];
    private readonly Dictionary<(string Text, Font Font, float Width), Font> _fitted = [];

    public UiPainter(PanelStyle style, float scale)
    {
        Style = style;
        Scale = scale;

        Format = (StringFormat)StringFormat.GenericTypographic.Clone();
        Format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;

        _measureSurface = new Bitmap(1, 1);
        _measure = Graphics.FromImage(_measureSurface);
        _measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        Fonts = new FontSet(
            titleSize: 14f * scale,
            auxSize: 13.5f * scale,
            countdownSize: 13.5f * scale,
            resetLabelSize: 13f * scale,
            absenceSize: 12f * scale,
            idSize: 13.5f * scale);

        // 目立たせる数値の部品（リセットまでの時間→実装メモ5.58）。2026-09-27に名前と値を1.2倍にした（16→19.2px・19→22.8px→5.68）。
        ProminentLabel = new Font(Fonts.JapaneseFamilyName, 19.2f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        // 値は 2026-09-27 にさらに1.15倍にした（22.8→26.22px→5.70）。
        ProminentValue = new Font(Fonts.MonospaceFamilyName, ProminentValueSize * scale, FontStyle.Bold, GraphicsUnit.Pixel);

        // 値の単位（「分」）は値の0.6倍（→実装メモ5.69）。
        ProminentUnit = new Font(Fonts.JapaneseFamilyName, ProminentValueSize * 0.6f * scale, FontStyle.Bold, GraphicsUnit.Pixel);

        // インスタンス詳細の先頭の見出しは、帯と同じく1.3倍（→実装メモ5.69）。
        VisitTitleMono = new Font(Fonts.MonospaceFamilyName, Fonts.AuxMono.Size * 1.3f, Fonts.AuxMono.Style, GraphicsUnit.Pixel);
        VisitTitleJapanese = new Font(Fonts.JapaneseFamilyName, Fonts.Aux.Size * 1.3f, Fonts.Aux.Style, GraphicsUnit.Pixel);
    }

    /// <summary>目立たせる項目の値の単位（「分」）。</summary>
    public Font ProminentUnit { get; }

    /// <summary>インスタンス詳細の先頭の見出しのインスタンス番号。</summary>
    public Font VisitTitleMono { get; }

    /// <summary>インスタンス詳細の先頭の見出しの日本語（「滞在中」・ワールド名・タイプ）。</summary>
    public Font VisitTitleJapanese { get; }

    public PanelStyle Style { get; }

    /// <summary>論理pxに掛ける倍率。</summary>
    public float Scale { get; }

    /// <summary>
    /// 字の割り当て。<see cref="FontSet.Title"/> はまとまりの見出し、<see cref="FontSet.Aux"/> は項目名、
    /// <see cref="FontSet.AuxMono"/> は値、<see cref="FontSet.ResetLabel"/> はボタン、<see cref="FontSet.Absence"/> は説明。
    /// </summary>
    public FontSet Fonts { get; }

    public StringFormat Format { get; }

    /// <summary>目立たせる項目の名前（リセットまでの時間→実装メモ5.58）。</summary>
    public Font ProminentLabel { get; }

    /// <summary>目立たせる項目の値。</summary>
    public Font ProminentValue { get; }

    public float S(float logical) => logical * Scale;

    public float MeasureWidth(string text, Font font)
        => text.Length == 0 ? 0f : _measure.MeasureString(text, font, PointF.Empty, Format).Width;

    /// <summary>フォントの上端からベースラインまで（px）。大きさの違う字のベースラインをそろえるのに使う（→実装メモ5.37）。</summary>
    public static float Ascent(Font font)
    {
        var family = font.FontFamily;
        return font.Size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
    }

    /// <summary>
    /// その色の単色の筆。描き手が持っていて使い回すので、受け取った側で手放さない（<c>using</c> にしない）。
    /// 状態の段の点の点滅（→実装メモ5.91）のように、短い間隔で何度も描くところで筆を作り直さないため。
    /// </summary>
    public SolidBrush Brush(Color color)
    {
        var key = color.ToArgb();

        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidBrush(color);
            _brushes[key] = brush;
        }

        return brush;
    }

    /// <summary>枠線はペンではなく塗りつぶしで描く（1px幅のペンは辺の半分が外へ出る→実装メモ5.18）。</summary>
    public void DrawBorder(Graphics graphics, RectangleF rect, Color color)
        => FillBorder(graphics, Brush(color), rect, MathF.Max(1f, MathF.Round(S(BorderWidth))));

    /// <summary><paramref name="rect"/> の内側に、幅 <paramref name="width"/> の枠を塗る（4辺とも矩形の内側に収める→実装メモ5.18）。</summary>
    public static void FillBorder(Graphics graphics, Brush brush, RectangleF rect, float width)
    {
        graphics.FillRectangle(brush, rect.X, rect.Y, rect.Width, width);
        graphics.FillRectangle(brush, rect.X, rect.Bottom - width, rect.Width, width);
        graphics.FillRectangle(brush, rect.X, rect.Y, width, rect.Height);
        graphics.FillRectangle(brush, rect.Right - width, rect.Y, width, rect.Height);
    }

    /// <summary>
    /// 枠（設定のまとまり・インスタンス詳細）の地。本文（Surface）を塗り、<paramref name="bandHeight"/> が正なら上に見出しの帯（Header）と
    /// その下の区切りの線を引く。見出しの字は描く側が置く。
    /// </summary>
    public void DrawCard(Graphics graphics, RectangleF rect, float bandHeight)
    {
        graphics.FillRectangle(Brush(Style.Surface), rect);

        if (bandHeight <= 0f)
            return;

        graphics.FillRectangle(Brush(Style.Header), rect.X, rect.Y, rect.Width, bandHeight);
        graphics.FillRectangle(Brush(Style.Rule), rect.X, rect.Y + bandHeight, rect.Width, MathF.Max(1f, MathF.Round(S(BorderWidth))));
    }

    /// <summary>
    /// ボタン。見出しの「延長」と同じく、指している間は枠と文字をアクセント色にする。
    /// 押せないとき（<paramref name="enabled"/> が false）は、枠と文字を区切りの線の色にする。
    /// </summary>
    public void DrawButton(Graphics graphics, RectangleF rect, string label, bool pointed, bool enabled = true)
    {
        pointed &= enabled;

        if (pointed)
            graphics.FillRectangle(Brush(Style.CurrentRow), rect);

        var color = !enabled ? Style.Rule : pointed ? Style.Accent : Style.Muted;
        DrawBorder(graphics, rect, color);

        var font = Fonts.ResetLabel;
        var width = MeasureWidth(label, font);
        graphics.DrawString(label, font, Brush(color), rect.X + ((rect.Width - width) / 2f), rect.Y + ((rect.Height - font.GetHeight(graphics)) / 2f), Format);
    }

    /// <summary>
    /// 収まらない文字列を、末尾を切らずに右端へ向けて画素単位で透明にして描く（パネルの行と同じ見せ方→実装メモ5.12b・5.22）。
    /// 収まるときは単色で描く。
    /// </summary>
    public void DrawFadingText(Graphics graphics, string text, Font font, Color color, float x, float y, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            return;

        if (MeasureWidth(text, font) <= maxWidth)
        {
            graphics.DrawString(text, font, Brush(color), x, y, Format);
            return;
        }

        var fadeStart = Math.Max(0f, maxWidth - S(FadeWidth));
        var height = font.GetHeight(graphics) + 2f;

        var state = graphics.Save();
        graphics.SetClip(new RectangleF(x, y, maxWidth, height), CombineMode.Intersect);

        var area = new RectangleF(x - 1f, y - 1f, maxWidth + 2f, height + 2f);
        var transparent = Color.FromArgb(0, color);

        using (var brush = new LinearGradientBrush(area, color, transparent, LinearGradientMode.Horizontal))
        {
            brush.InterpolationColors = new ColorBlend
            {
                Colors = [color, color, transparent],
                Positions = [0f, (fadeStart + 1f) / area.Width, 1f],
            };

            graphics.DrawString(text, font, brush, x, y, Format);
        }

        graphics.Restore(state);
    }

    /// <summary>
    /// 文字列を <paramref name="maxWidth"/> に収まるよう、文字の境目で行に分ける（最大 <paramref name="maxLines"/> 行）。
    /// 入り切らない分は最後の行に残し、描く側が右端でフェードさせる。
    /// 1文字ずつ測るので重い。同じ文字列・幅なら前に分けた結果を返す（状態の段の詳しい文は、指している間ずっと描き直す）。
    /// </summary>
    public IReadOnlyList<string> Wrap(string text, Font font, float maxWidth, int maxLines)
    {
        var key = (text, font, maxWidth, maxLines);

        if (_wrapped.TryGetValue(key, out var cached))
            return cached;

        if (_wrapped.Count >= MaxWrapCache)
            _wrapped.Clear();

        var lines = WrapUncached(text, font, maxWidth, maxLines);
        _wrapped[key] = lines;
        return lines;
    }

    private List<string> WrapUncached(string text, Font font, float maxWidth, int maxLines)
    {
        var lines = new List<string>();
        var rest = text;

        while (rest.Length > 0 && lines.Count < maxLines - 1)
        {
            if (MeasureWidth(rest, font) <= maxWidth)
                break;

            // 収まる最長の長さを探す（サロゲートペアの途中では切らない）。
            var length = 1;
            while (length < rest.Length && MeasureWidth(rest[..NextBoundary(rest, length)], font) <= maxWidth)
                length = NextBoundary(rest, length);

            if (length < rest.Length && char.IsLowSurrogate(rest[length]))
                length++;

            lines.Add(rest[..length]);
            rest = rest[length..];
        }

        if (rest.Length > 0)
            lines.Add(rest);

        return lines;
    }

    private static int NextBoundary(string text, int index)
    {
        var next = index + 1;
        return next < text.Length && char.IsLowSurrogate(text[next]) ? next + 1 : next;
    }

    /// <summary>
    /// <paramref name="text"/> が <paramref name="width"/> に入る字。入るならそのまま <paramref name="font"/> を、
    /// 入らなければ入る大きさに縮めた字を返す（縮めた字は描き手が持つので、受け取った側で手放さない）。
    /// </summary>
    public Font FittedFont(string text, Font font, float width)
    {
        var measured = MeasureWidth(text, font);

        if (measured <= width || measured <= 0f)
            return font;

        var key = (text, font, width);

        if (!_fitted.TryGetValue(key, out var fitted))
        {
            fitted = new Font(font.FontFamily, font.Size * (width / measured), font.Style, GraphicsUnit.Pixel);
            _fitted[key] = fitted;
        }

        return fitted;
    }

    public void Dispose()
    {
        foreach (var brush in _brushes.Values)
            brush.Dispose();

        foreach (var font in _fitted.Values)
            font.Dispose();

        _brushes.Clear();
        _fitted.Clear();
        _wrapped.Clear();

        _measure.Dispose();
        _measureSurface.Dispose();
        Format.Dispose();
        ProminentLabel.Dispose();
        ProminentValue.Dispose();
        ProminentUnit.Dispose();
        VisitTitleMono.Dispose();
        VisitTitleJapanese.Dispose();
        Fonts.Dispose();
    }
}
