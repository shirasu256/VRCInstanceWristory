using System.Drawing;
using System.Drawing.Drawing2D;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// ウィンドウの左下の状態の段（2026-09-28のユーザー指定→実装メモ5.85）と、その右端の開発・実装の表示（→実装メモ5.71）。
///
/// 4つの項目（<see cref="StatusText.Build"/>）を2つずつ2行に並べ、項目の頭の点は赤・黄なら点滅させる（→実装メモ5.91）。
/// 指した項目に詳しい文があれば（または値が切れていれば）、段のすぐ上に吹き出しで出す。
/// 置く側（<see cref="DesktopView"/>）が矩形と描き手を渡して並べさせ、マウスの位置を知らせる。
/// </summary>
internal sealed class StatusBar
{
    // 寸法は論理px（96dpi）。

    /// <summary>1行の高さ。</summary>
    private const float LineHeight = 20f;

    /// <summary>行の数と、1行に並べる項目の数。</summary>
    private const int Lines = 2;

    private const int ItemsPerLine = 2;

    /// <summary>項目の頭の点の大きさと、点と名前の間。</summary>
    private const float DotSize = 7f;

    private const float DotGap = 7f;

    /// <summary>名前と値の間。</summary>
    private const float LabelGap = 8f;

    /// <summary>1行の2つの項目の間。</summary>
    private const float ItemGap = 20f;

    /// <summary>右端の表示と、その手前で切る値の間。</summary>
    private const float CreditGap = 16f;

    /// <summary>2つが入り切らないとき、2つ目に残す幅の下限。</summary>
    private const float MinSecondWidth = 150f;

    /// <summary>ステータスの段の右端（→実装メモ5.71）。</summary>
    private const string CreditDeveloperLabel = "開発: ";

    private const string CreditDeveloperName = "shirasu256";

    private const string CreditImplementation = "実装: Claude Code by Anthropic";

    private static readonly System.Diagnostics.Stopwatch BlinkWatch = System.Diagnostics.Stopwatch.StartNew();

    private readonly PanelStyle _style;
    private readonly RectangleF[] _slots = new RectangleF[Lines * ItemsPerLine];

    // 右端の表示の、行ごとの左端（1行目「開発: …」は短いので、状態の値はそのぶん右まで出せる）。
    private readonly float[] _creditLineLeft = new float[Lines];

    private UiPainter? _painter;
    private StatusItem[] _items = StatusText.Build(DesktopStatus.Initial);
    private DesktopStatus _status = DesktopStatus.Initial;
    private RectangleF _rect;
    private RectangleF _creditLinkRect;
    private bool _creditPointed;

    // 指している項目（-1 なら指していない）。
    private int _pointed = -1;

    public StatusBar(PanelStyle style) => _style = style;

    /// <summary>段の高さ（画素）。</summary>
    public static float Height(float scale) => LineHeight * scale * Lines;

    /// <summary>点滅の時計（→実装メモ5.91）。見本の画像と自動検証では 0 に固定して、点いた状態で描く。</summary>
    public Func<TimeSpan> BlinkClock { get; set; } = static () => BlinkWatch.Elapsed;

    /// <summary>4つの項目（左上から「VRChat」「VRChat ログ」「SteamVR」「手首パネル」）。</summary>
    public IReadOnlyList<StatusItem> Items => _items;

    /// <summary>点滅する点（赤・黄）があるか。</summary>
    public bool Blinking => _items.Any(item => StatusBlink.Blinks(item.Tone));

    /// <summary>開発者のリンクの矩形。</summary>
    public RectangleF CreditLinkRect => _creditLinkRect;

    /// <summary>項目の矩形。</summary>
    public RectangleF Slot(int index) => _slots[index];

    /// <summary>詳しい文を出しているなら、その項目の番号。</summary>
    public int? PointedDetail => _pointed >= 0 && DetailOf(_pointed) is not null ? _pointed : null;

    private UiPainter Painter => _painter ?? throw new InvalidOperationException("Layout の前です。");

    private float S(float logical) => Painter.S(logical);

    /// <summary>状態を差し替える。変わったら true（置く側が並べ直して描き直す）。</summary>
    public bool SetStatus(DesktopStatus status)
    {
        if (status == _status)
            return false;

        _status = status;
        _items = StatusText.Build(status);
        return true;
    }

    /// <summary><paramref name="rect"/> の中へ並べる。倍率が変わって描き手を作り直したときも、古いものを手放す前にここで渡し直す。</summary>
    public void Layout(RectangleF rect, UiPainter painter)
    {
        _rect = rect;
        _painter = painter;
        LayoutCredit();
        LayoutItems();
    }

    /// <summary>項目の値が変わった。1列目の幅は値の長さで決まるので、置き場所を決め直す。</summary>
    public void RelayoutItems() => LayoutItems();

    /// <summary>マウスの位置（null なら外れた）。見た目が変わったら true。</summary>
    public bool PointerMove(PointF? mouse)
    {
        var before = (_pointed, _creditPointed);
        _creditPointed = mouse is { } m && _creditLinkRect.Contains(m);
        _pointed = mouse is { } p ? Array.FindIndex(_slots, r => r.Contains(p)) : -1;
        return before != (_pointed, _creditPointed);
    }

    /// <summary>ステータスの段の右端の表示の、行ごとの左端とリンクの矩形を決める（→実装メモ5.71）。</summary>
    private void LayoutCredit()
    {
        var font = Painter.Fonts.Absence;
        var labelWidth = Painter.MeasureWidth(CreditDeveloperLabel, font);
        var nameWidth = Painter.MeasureWidth(CreditDeveloperName, font);

        _creditLineLeft[0] = _rect.Right - (labelWidth + nameWidth);
        _creditLineLeft[1] = _rect.Right - Painter.MeasureWidth(CreditImplementation, font);

        // 1行目は右端にそろえる（2行目とは右端をそろえ、左端はそれぞれ）。
        _creditLinkRect = new RectangleF(_rect.Right - nameWidth, _rect.Y, nameWidth, S(LineHeight));
    }

    /// <summary>
    /// 4項目の置き場所（→実装メモ5.85）。2つずつ2行に並べ、行ごとに左から詰める。
    /// 右端の表示（開発・実装）に掛かる値は末尾を薄くする。2つが入り切らないときは、2つ目に全体の半分近くまでを残して1つ目を切る
    /// （切れた値の全文は、指したときに出す）。
    /// </summary>
    private void LayoutItems()
    {
        var font = Painter.Fonts.Aux;
        var lineHeight = S(LineHeight);
        var gap = S(ItemGap);

        for (var line = 0; line < Lines; line++)
        {
            var first = line * ItemsPerLine;
            var second = first + 1;
            var limit = MathF.Max(_rect.X, _creditLineLeft[line] - S(CreditGap));
            var available = limit - _rect.X;
            var y = _rect.Y + (line * lineHeight);
            var firstWidth = ItemWidth(_items[first], font);
            var secondWidth = MathF.Min(ItemWidth(_items[second], font), MathF.Max(S(MinSecondWidth), available * 0.45f));
            var secondX = MathF.Min(_rect.X + firstWidth + gap, MathF.Max(_rect.X, limit - secondWidth));

            _slots[first] = new RectangleF(_rect.X, y, MathF.Max(0f, secondX - gap - _rect.X), lineHeight);
            _slots[second] = new RectangleF(secondX, y, MathF.Max(0f, limit - secondX), lineHeight);
        }
    }

    /// <summary>項目の点・名前・値を並べた幅（足し合わせの丸めで値の末尾が薄くならないよう、わずかに余裕を持たせる）。</summary>
    private float ItemWidth(StatusItem item, Font font)
        => S(DotSize) + S(DotGap) + Painter.MeasureWidth(item.Label, font) + S(LabelGap) + Painter.MeasureWidth(item.Value, font) + S(2f);

    /// <summary>その項目を指したときに出すもの。詳しい文がなく、値も枠に収まっていれば null。</summary>
    private StatusItem? DetailOf(int index)
    {
        var item = _items[index];

        if (item.Detail is not null)
            return item;

        return ItemWidth(item, Painter.Fonts.Aux) > _slots[index].Width ? item : null;
    }

    // ------------------------------------------------------------------ 描画

    /// <summary>4項目と右端の表示。</summary>
    public void Render(Graphics graphics)
    {
        var font = Painter.Fonts.Aux;
        var lineHeight = S(LineHeight);
        var now = BlinkClock();
        var muted = Painter.Brush(_style.Muted);

        for (var i = 0; i < _items.Length; i++)
        {
            var (label, value, _, _) = _items[i];
            var slot = _slots[i];

            DrawDot(graphics, i, now);

            var textY = slot.Y + ((lineHeight - font.GetHeight(graphics)) / 2f);
            var labelX = slot.X + S(DotSize) + S(DotGap);
            graphics.DrawString(label, font, muted, labelX, textY, Painter.Format);

            var valueX = labelX + Painter.MeasureWidth(label, font) + S(LabelGap);

            // 右端の表示に掛かる値は、末尾を薄くして収める（全文は指したときに出す）。
            Painter.DrawFadingText(graphics, value, font, _style.Text, valueX, textY, MathF.Max(0f, slot.Right - valueX));
        }

        RenderCredit(graphics);
    }

    /// <summary>
    /// 点滅する点だけを描き直す（→実装メモ5.91）。ウィンドウ全体を描き直すと重いので、点の周りを地の色で塗ってから点を描く。
    /// 描き直した範囲（画素）を返す。点滅する点がなければ null。
    /// 状態の詳しい文の吹き出しは状態の段より上に出るので、点とは重ならない。
    /// </summary>
    public Rectangle? RenderBlinkingDots(Graphics graphics)
    {
        var now = BlinkClock();
        Rectangle? area = null;

        for (var i = 0; i < _items.Length; i++)
        {
            if (!StatusBlink.Blinks(_items[i].Tone))
                continue;

            var rect = Rectangle.Ceiling(RectangleF.Inflate(DotRect(i), 2f, 2f));
            graphics.FillRectangle(Painter.Brush(UiMetrics.WindowBackground), rect);

            DrawDot(graphics, i, now);
            area = area is { } union ? Rectangle.Union(union, rect) : rect;
        }

        return area;
    }

    /// <summary>
    /// 指している項目の詳しい文（→実装メモ5.85）。段のすぐ上に枠を出し、1行目に項目と値の全文、その下に詳しい文を折り返して出す。
    /// 詳しい文がなく、値も切れていない項目では出さない。
    /// </summary>
    public void RenderDetail(Graphics graphics)
    {
        if (_pointed < 0 || DetailOf(_pointed) is not { } text)
            return;

        var font = Painter.Fonts.Aux;
        var padding = S(10f);
        var lineHeight = font.GetHeight(graphics) + S(3f);
        var width = MathF.Min(S(460f), MathF.Max(S(200f), _rect.Width));
        var lines = Painter.Wrap(text.Detail ?? string.Empty, font, width - (padding * 2f), maxLines: 6);
        var height = (padding * 2f) + lineHeight + (lines.Count > 0 ? S(4f) + (lines.Count * lineHeight) : 0f);

        var slot = _slots[_pointed];
        var x = Math.Clamp(slot.X - S(4f), _rect.X, MathF.Max(_rect.X, _rect.Right - width));
        var box = new RectangleF(x, _rect.Y - S(6f) - height, width, height);

        graphics.FillRectangle(Painter.Brush(_style.Header), box);

        // 1px の枠は FillRectangle で描く（ペンの線は辺の中心に乗るため→実装メモ5.18）。
        UiPainter.FillBorder(graphics, Painter.Brush(ToneColor(text.Tone)), box, MathF.Max(1f, S(1f)));

        var y = box.Y + padding;
        graphics.DrawString(text.Label, font, Painter.Brush(_style.Muted), box.X + padding, y, Painter.Format);

        var valueX = box.X + padding + Painter.MeasureWidth(text.Label, font) + S(LabelGap);
        Painter.DrawFadingText(graphics, text.Value, font, _style.Text, valueX, y, MathF.Max(0f, box.Right - padding - valueX));
        y += lineHeight + S(4f);

        var body = Painter.Brush(_style.Text);

        foreach (var line in lines)
        {
            graphics.DrawString(line, font, body, box.X + padding, y, Painter.Format);
            y += lineHeight;
        }
    }

    /// <summary>項目の点の矩形。</summary>
    private RectangleF DotRect(int index)
    {
        var dot = S(DotSize);
        var slot = _slots[index];
        return new RectangleF(slot.X, slot.Y + ((S(LineHeight) - dot) / 2f), dot, dot);
    }

    /// <summary>点を1つ描く。点滅する色は、そのときの不透明度を掛ける。</summary>
    private void DrawDot(Graphics graphics, int index, TimeSpan now)
    {
        var tone = _items[index].Tone;
        var color = ToneColor(tone);
        var alpha = StatusBlink.Alpha(tone, now);

        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // 不透明度は 0〜255 の整数に丸めるので、使い回す筆の数は色ごとに限られる。
        graphics.FillEllipse(Painter.Brush(Color.FromArgb((int)MathF.Round(color.A * alpha), color)), DotRect(index));

        graphics.Restore(state);
    }

    /// <summary>点の色。薄いアクセント色は、正常に動いていて、いまはパネルを出す場面ではないだけのとき（→実装メモ5.85）。</summary>
    private Color ToneColor(StatusTone tone) => tone switch
    {
        StatusTone.Good => _style.Accent,
        StatusTone.Quiet => Color.FromArgb(110, _style.Accent),
        StatusTone.Warning => _style.MarkWarning,
        StatusTone.Error => _style.Crash,
        _ => _style.Rule,
    };

    /// <summary>
    /// ステータスの段の右端（2026-09-27のユーザー指定→実装メモ5.71）。1行目「開発: shirasu256」、2行目「実装: Claude Code by Anthropic」。
    /// 名前は Group ID と同じ控えめなリンク（ふだんは薄い色、指すとアクセント色。塗りも下線も付けない）。
    /// </summary>
    private void RenderCredit(Graphics graphics)
    {
        var font = Painter.Fonts.Absence;
        var lineHeight = S(LineHeight);
        var textY = (lineHeight - font.GetHeight(graphics)) / 2f;
        var muted = Painter.Brush(_style.Muted);

        graphics.DrawString(CreditDeveloperLabel, font, muted, _creditLinkRect.X - Painter.MeasureWidth(CreditDeveloperLabel, font), _rect.Y + textY, Painter.Format);
        graphics.DrawString(CreditDeveloperName, font, Painter.Brush(_creditPointed ? _style.Accent : _style.Muted), _creditLinkRect.X, _rect.Y + textY, Painter.Format);
        graphics.DrawString(CreditImplementation, font, muted, _creditLineLeft[1], _rect.Y + lineHeight + textY, Painter.Format);
    }
}
