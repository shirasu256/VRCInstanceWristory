using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 履歴パネルの文字テクスチャを生成する（仕様8.1節）。
/// 背景の不透明度は背景画素にだけ適用し、文字は不透明のまま描く。
///
/// 高さは行数で変わる（最大7.5行、それ未満なら行のぶんだけ）。
/// ビットマップは上限の高さで1枚だけ確保しておき、渡すのは上から
/// <see cref="Height"/> 行ぶんだけにする。行数が変わるたびにGDI+の面を作り直さずに済む。
///
/// 見出しは <c>PanelRenderer.Header.cs</c>、行は <c>PanelRenderer.Rows.cs</c>、
/// 行の上に重ねるもの（指している行の帯・目印のポップアップ・履歴リセットの確認）は <c>PanelRenderer.Overlays.cs</c> にある。
/// </summary>
public sealed partial class PanelRenderer : IDisposable
{
    private readonly PanelStyle _style;
    private readonly FontSet _fonts;
    private readonly Bitmap _bitmap;
    private readonly Graphics _graphics;
    private readonly StringFormat _format;
    private byte[] _pixels;

    // 行だけを描いた下絵。スクロールは、ここから見えている窓を写すだけで行う。
    // GDI+の文字描画がいちばん重いので、行の内容が変わったときだけ描き直す。
    private Bitmap? _rowsBitmap;
    private Graphics? _rowsGraphics;

    // 下絵に描いてある行数と合計の高さ。見出しの件数とスクロールの判定に使う。
    private int _rowCount;
    private float _contentHeight;

    // 先頭の行（いちばん古い訪問）へ入った時刻。見出しの「(全8件 00:14~)」に出す。
    private string? _since;

    // 同じ時刻の日付付き（`2026-09-27 16:24`）。履歴の自動リセットを無効にしている間に出す（→実装メモ5.71）。
    private string? _sinceWithDate;

    // 測った文字の幅とフォントの高さ。フォントはこの描き手の間変わらないので、同じ文字を測り直さない
    // （見出しのボタンの矩形は、レイがパネルに当たっている間は毎フレーム求める）。
    private readonly Dictionary<Font, Dictionary<string, float>> _widths = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Font, float> _fontHeights = new(ReferenceEqualityComparer.Instance);
    private SizeF? _buttonSize;

    /// <summary>1つのフォントで覚えておく幅の数の上限。行の文字（インスタンス番号・ワールド名）は入れ替わっていくので、超えたら忘れる。</summary>
    private const int WidthCacheLimit = 1024;

    // 塗りとペン。色（ARGB）ごとに作って使い回す。背景の不透明度が変わったら作り直す（→ForgetStaleBrushes）。
    private readonly Dictionary<int, SolidBrush> _brushes = [];
    private readonly Dictionary<int, Pen> _pens = [];
    private byte _brushesAlpha;

    /// <summary>
    /// 最後に描いた見出しで、履歴の自動リセットを使っていたか（→実装メモ5.71）。<see cref="Compose"/> が控える。
    /// 残り時間の文字だけを受ける矩形の計算（<see cref="ResetButtonRectFor(string?)"/> など）と <see cref="HeaderTitleText"/> がこれを使う。
    /// </summary>
    /// <remarks>
    /// 手首のパネルもデスクトップのウィンドウも、命中を見るときは見出しの状態を <see cref="PanelDecorations"/> で渡す版
    /// （<see cref="ResetButtonRectFor(in PanelDecorations)"/> など）を使う。こちらは描いた絵を確かめる自動検証のためのもの。
    /// </remarks>
    public bool AutoResetEnabled { get; private set; } = true;

    /// <summary>最後に描いた見出しで、カウントダウンが止まっていたか（→実装メモ5.73）。<see cref="AutoResetEnabled"/> と同じく <see cref="Compose"/> が控える。</summary>
    public bool CountdownStopped { get; private set; }

    /// <summary>最後に描いた見出しで、該当する履歴がなかったか（→実装メモ5.131）。<see cref="AutoResetEnabled"/> と同じく <see cref="Compose"/> が控える。</summary>
    public bool HistoryEmpty { get; private set; }

    public PanelRenderer(PanelStyle style)
    {
        _style = style;
        _fonts = new FontSet(style);
        // 面は上限の高さで確保しておき、実際に使う高さだけを切り出して渡す。
        _bitmap = new Bitmap(style.Width, style.MaxHeight, PixelFormat.Format32bppArgb);
        _graphics = Graphics.FromImage(_bitmap);
        // 矩形と直線だけなので、背景の不透明度が縁で薄まらないようアンチエイリアスは使わない。
        // 文字の縁は TextRenderingHint 側で滑らかにする。
        _graphics.SmoothingMode = SmoothingMode.None;
        _graphics.PixelOffsetMode = PixelOffsetMode.Half;
        _graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        _graphics.CompositingQuality = CompositingQuality.HighQuality;

        // ClearTypeは半透明の背景で色付きの縁を作るため、グレースケールのアンチエイリアスを使う。
        _graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        _format = (StringFormat)StringFormat.GenericTypographic.Clone();
        _format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        _format.Trimming = StringTrimming.None;

        ViewportHeight = style.MaxViewportHeight;
        _pixels = new byte[style.Width * Height * 4];
    }

    public PanelStyle Style => _style;

    public FontSet Fonts => _fonts;

    public Bitmap Bitmap => _bitmap;

    public int Width => _style.Width;

    /// <summary>
    /// いま取っている履歴の表示領域の高さ（px）。行が7.5行ぶんに満たないときは、その行のぶんまで縮む。
    /// </summary>
    public int ViewportHeight { get; private set; }

    /// <summary>いまの枠テクスチャの高さ（px）。見出し・<see cref="ViewportHeight"/>・下部案内の合計。</summary>
    public int Height => _style.HeightFor(ViewportHeight);

    /// <summary>
    /// 行の合計の高さに合わせて表示領域を詰める（上限は7.5行ぶん）。
    /// 変わったときだけ true を返す。呼び出し側はオーバーレイの配置を取り直す。
    /// </summary>
    public bool SetViewportForContent(float contentHeight)
        => SetViewportHeight(_style.ViewportHeightFor(contentHeight));

    /// <summary>
    /// 表示領域の高さを決める（標準行1行ぶん〜上限）。デスクトップのウィンドウは、行数ではなくウィンドウの高さで決める（→実装メモ5.71）。
    /// 変わったときだけ true。
    /// </summary>
    public bool SetViewportHeightTo(int viewportHeight)
        => SetViewportHeight(Math.Clamp(viewportHeight, _style.RowHeight, _style.MaxViewportHeight));

    private bool SetViewportHeight(int viewportHeight)
    {
        if (viewportHeight == ViewportHeight)
            return false;

        ViewportHeight = viewportHeight;

        // 渡す配列は常に実際の高さぴったりにしておく（余りを送らない）。
        _pixels = new byte[Width * Height * 4];
        return true;
    }

    /// <summary>
    /// パネルを1枚に組み立てる。
    ///
    /// 行は <see cref="RenderRows"/> が描いた下絵から、いま見えている窓を写すだけなので、
    /// スクロールしても文字を描き直さない（GDI+の文字描画がいちばん重いため）。
    /// 見出し・つまみ・指している行の帯・目印のポップアップはここで直接描く。
    /// </summary>
    /// <param name="scrollOffset">内容座標での表示領域の上端。</param>
    public void Compose(float scrollOffset, in PanelDecorations decorations = default)
    {
        // 残り時間の文字だけを受ける矩形の計算のために、描いた見出しの状態を控えておく。描くのは decorations の値だけで行う。
        AutoResetEnabled = !decorations.AutoResetDisabled;
        CountdownStopped = decorations.CountdownStopped;
        HistoryEmpty = decorations.HistoryEmpty;

        ForgetStaleBrushes();
        var alpha = _style.BackgroundAlpha;

        // 背景は重ね塗りせず置き換える。重ねると不透明度が設定値より高くなるため。
        _graphics.CompositingMode = CompositingMode.SourceCopy;

        var surface = BrushFor(Color.FromArgb(alpha, _style.Surface));
        _graphics.FillRectangle(BrushFor(Color.FromArgb(alpha, _style.Header)), 0, 0, Width, _style.HeaderHeight);
        _graphics.FillRectangle(surface, 0, _style.ViewportTop, Width, ViewportHeight);
        _graphics.FillRectangle(surface, 0, Height - _style.FooterHeight, Width, _style.FooterHeight);

        // 行の下絵から、いま見えている窓をそのまま写す（重ねずに置き換える）。
        if (_rowsBitmap is not null)
        {
            var top = (int)MathF.Round(Math.Clamp(scrollOffset, 0f, MathF.Max(0, _rowsBitmap.Height - ViewportHeight)));

            _graphics.DrawImage(
                _rowsBitmap,
                new Rectangle(0, _style.ViewportTop, Width, ViewportHeight),
                new Rectangle(0, top, Width, ViewportHeight),
                GraphicsUnit.Pixel);
        }

        _graphics.CompositingMode = CompositingMode.SourceOver;

        // 行が1つもなければ、表示領域の中央に知らせる（→実装メモ5.128）。
        if (_rowCount == 0)
            DrawEmptyMessage();

        // 指している行の帯は行の上・見出しの下。
        if (decorations.HoverRow.Height >= 1f)
            DrawRowHighlight(_graphics, decorations.HoverRow);

        // デスクトップのウィンドウで選んでいる行は、アクセント色の枠で囲む（→実装メモ5.42）。
        if (decorations.SelectedRow.Height >= 1f)
            DrawRowSelection(_graphics, decorations.SelectedRow);

        var rule = PenFor(Color.FromArgb(alpha, _style.Rule));
        _graphics.DrawLine(rule, 0, _style.HeaderHeight, Width, _style.HeaderHeight);
        _graphics.DrawLine(rule, 0, Height - _style.FooterHeight, Width, Height - _style.FooterHeight);

        DrawHeader(decorations);
        DrawFooter(decorations.UpdateAvailable);
        DrawScrollBar(scrollOffset);

        // 目印のポップアップは行の手前。履歴リセットの確認はさらにその手前（→実装メモ5.65）。
        if (!decorations.Popup.IsEmpty)
            DrawPopup(decorations);

        if (decorations.ConfirmClear)
            DrawClearConfirm(_graphics, decorations.ConfirmPointed);
    }

    /// <summary>
    /// 下部の右にアプリ名と版を出す。左には何も出さない（2026-09-19のユーザー指定）。
    /// 新しいバージョンが公開されていれば、アプリ名と版の左に「新バージョンが公開されています」を出す（2026-10-01のユーザー指定→実装メモ5.122）。
    /// </summary>
    private void DrawFooter(bool updateAvailable)
    {
        var y = Height - _style.FooterHeight + (_style.FooterHeight - FontHeight(_fonts.Aux)) / 2f;

        var right = AppInfo.NameWithVersion;
        var width = MeasureWidth(right, _fonts.Aux);
        var x = Width - _style.PaddingRight - _style.ScrollBarWidth - width;
        _graphics.DrawString(right, _fonts.Aux, BrushFor(_style.Muted), x, y, _format);

        if (!updateAvailable)
            return;

        var notice = UpdateNotice;
        var noticeWidth = MeasureWidth(notice, _fonts.Aux);
        _graphics.DrawString(notice, _fonts.Aux, BrushFor(_style.Accent), x - _style.PaddingRight - noticeWidth, y, _format);
    }

    /// <summary>新しいバージョンが公開されているときに、アプリ名と版の左に出す文（→実装メモ5.122）。</summary>
    public const string UpdateNotice = "新バージョンが公開されています";

    /// <summary>
    /// 出す行がないときに表示領域へ書く文（2026-10-02のユーザー指定→実装メモ5.128）。
    /// 手首のパネル（設定「該当履歴が無い場合も表示する」がオンのとき）とデスクトップのウィンドウで同じ文を使う。
    /// </summary>
    public const string EmptyMessage = "該当する履歴はありません";

    private void DrawEmptyMessage()
    {
        var font = _fonts.Aux;
        var x = (Width - MeasureWidth(EmptyMessage, font)) / 2f;
        var y = _style.ViewportTop + ((ViewportHeight - FontHeight(font)) / 2f);
        _graphics.DrawString(EmptyMessage, font, BrushFor(_style.Muted), x, y, _format);
    }


    private void DrawScrollBar(float scrollOffset)
    {
        var content = _contentHeight;
        if (content <= ViewportHeight)
            return;

        _graphics.FillRectangle(BrushFor(Color.FromArgb(_style.BackgroundAlpha / 2, _style.ScrollTrack)), PanelGeometry.ScrollTrackRect(_style, ViewportHeight));
        _graphics.FillRectangle(BrushFor(_style.ScrollTrack), PanelGeometry.ScrollThumbRect(_style, ViewportHeight, content, scrollOffset));
    }


    // ------------------------------------------------------------------ 測る

    private float MeasureWidth(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        if (!_widths.TryGetValue(font, out var widths))
            _widths[font] = widths = new Dictionary<string, float>(StringComparer.Ordinal);

        if (widths.TryGetValue(text, out var width))
            return width;

        if (widths.Count >= WidthCacheLimit)
            widths.Clear();

        width = _graphics.MeasureString(text, font, int.MaxValue, _format).Width;
        widths[text] = width;
        return width;
    }

    /// <summary>フォントの1行の高さ（px・この描き手の面で測る）。</summary>
    private float FontHeight(Font font)
    {
        if (!_fontHeights.TryGetValue(font, out var height))
            _fontHeights[font] = height = font.GetHeight(_graphics);

        return height;
    }

    /// <summary>フォントの上端からベースラインまで（px）。フォントの大きさは画素で指定してある。</summary>
    private static float Ascent(Font font)
    {
        var family = font.FontFamily;
        return font.Size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
    }

    // ------------------------------------------------------------------ 塗り

    /// <summary>その色の塗り。作って覚えておく（描くたびに作って捨てない）。</summary>
    private SolidBrush BrushFor(Color color)
    {
        var argb = color.ToArgb();

        if (!_brushes.TryGetValue(argb, out var brush))
            _brushes[argb] = brush = new SolidBrush(color);

        return brush;
    }

    /// <summary>その色の1pxのペン。</summary>
    private Pen PenFor(Color color)
    {
        var argb = color.ToArgb();

        if (!_pens.TryGetValue(argb, out var pen))
            _pens[argb] = pen = new Pen(color);

        return pen;
    }

    /// <summary>
    /// 背景の不透明度が変わっていたら、覚えている塗りを捨てる（不透明度の違う色が溜まっていかないように）。
    /// 塗りを手に持ったまま描いている途中では呼ばない（描き始めの <see cref="Compose"/>・<see cref="RenderRows"/> で呼ぶ）。
    /// </summary>
    private void ForgetStaleBrushes()
    {
        if (_style.BackgroundAlpha == _brushesAlpha)
            return;

        ForgetBrushes();
        _brushesAlpha = _style.BackgroundAlpha;
    }

    private void ForgetBrushes()
    {
        foreach (var brush in _brushes.Values)
            brush.Dispose();

        foreach (var pen in _pens.Values)
            pen.Dispose();

        _brushes.Clear();
        _pens.Clear();
    }

    /// <summary>
    /// 矩形の内側に沿った枠（太さ <paramref name="border"/>）。ペンではなく塗りつぶしで描く。
    /// 1px幅のペンは線の中心が矩形の辺に乗るため、辺がテクスチャの縁にあると線の半分が外へ出て消える。
    /// 実機では左辺と上辺だけが描かれなかった（2026-09-19の実機確認→実装メモ5.18）。塗りつぶしなら縁でも4辺が同じ太さで出る。
    /// </summary>
    private static void FillBorder(Graphics graphics, Brush brush, RectangleF rect, float border)
    {
        graphics.FillRectangle(brush, rect.X, rect.Y, rect.Width, border);
        graphics.FillRectangle(brush, rect.X, rect.Bottom - border, rect.Width, border);
        graphics.FillRectangle(brush, rect.X, rect.Y, border, rect.Height);
        graphics.FillRectangle(brush, rect.Right - border, rect.Y, border, rect.Height);
    }

    // ------------------------------------------------------------------ 画素

    /// <summary>
    /// 現在のビットマップの画素を取り出す。
    /// GDI+ のメモリ配置は BGRA で、D3D11のテクスチャ（B8G8R8A8_UNORM）と同じなので、
    /// 行ごとの複写だけで済む（→実装メモ5.35）。
    /// </summary>
    public byte[] GetPixels()
        => CopyPixels(_bitmap, _pixels, Height);

    /// <summary>
    /// ビットマップの画素（BGRA）を、用意した配列へ写す。
    /// <paramref name="rows"/> を渡すと上から その行数ぶんだけを写す（面より低いテクスチャを渡すため）。
    /// </summary>
    public static byte[] CopyPixels(Bitmap bitmap, byte[] destination, int? rows = null)
    {
        var width = bitmap.Width;
        var height = Math.Min(rows ?? bitmap.Height, bitmap.Height);

        // 配列の大きさは呼び出し側が決めるので、足りなければ写す前に止める（ポインターで書くので、はみ出すとヒープを壊す）。
        if (destination.Length < (long)width * height * 4)
            throw new ArgumentException($"画素の配列が小さすぎます（{destination.Length}バイト・{width}×{height}px には {(long)width * height * 4}バイト要る）。", nameof(destination));

        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var stride = data.Stride;
            var rowBytes = width * 4;

            unsafe
            {
                fixed (byte* destBase = destination)
                {
                    var source = (byte*)data.Scan0;

                    for (var y = 0; y < height; y++)
                    {
                        var sourceRow = source + (long)y * stride;
                        var destRow = destBase + (long)y * rowBytes;

                        Buffer.MemoryCopy(sourceRow, destRow, rowBytes, rowBytes);
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return destination;
    }

    /// <summary>
    /// 行の下絵の画素（BGRA）。自動検証で行の中身だけを見るために使う。
    /// 下絵をまだ描いていなければ空。
    /// </summary>
    public byte[] RowsPixels()
    {
        if (_rowsBitmap is null)
            return [];

        return CopyPixels(_rowsBitmap, new byte[Width * _rowsBitmap.Height * 4]);
    }

    /// <summary>いま使っている高さぶんだけを保存する（面の下の未使用部分は含めない）。</summary>
    public void SavePng(string path)
    {
        using var visible = _bitmap.Clone(new Rectangle(0, 0, Width, Height), PixelFormat.Format32bppArgb);
        visible.Save(path, ImageFormat.Png);
    }

    public void Dispose()
    {
        ForgetBrushes();
        _format.Dispose();
        _rowsGraphics?.Dispose();
        _rowsBitmap?.Dispose();
        _graphics.Dispose();
        _bitmap.Dispose();
        _fonts.Dispose();
    }
}
