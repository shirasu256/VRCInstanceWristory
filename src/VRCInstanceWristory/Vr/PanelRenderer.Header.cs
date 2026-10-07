using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace VRCInstanceWristory.Vr;

/// <summary>見出し（表題・残り時間・「延長」「履歴リセット」のボタン）。</summary>
public sealed partial class PanelRenderer
{
    /// <summary>
    /// 見出し右の1つ目のボタンに出す文字。押すと消去までの60分を数え直す（2026-09-19のユーザー指定）。
    /// 2026-09-25に「カウントリセット」から「カウント延長」へ、2026-09-27に「延長」へ改めた（→実装メモ5.36・5.65）。
    /// </summary>
    public const string ResetButtonLabel = "延長";

    /// <summary>
    /// 見出しの右端のボタンに出す文字（2026-09-27のユーザー指定→実装メモ5.65）。
    /// 押すと確認を挟んでから、訪問履歴をすべて今すぐ消す。
    /// </summary>
    public const string ClearButtonLabel = "履歴リセット";

    /// <summary>
    /// 残り時間の前に添える文字（2026-09-25のユーザー指定）。数字だけでは何までの時間か分からないため。
    /// 2026-09-27に「ログリセットまで:」から改めた（→実装メモ5.65）。
    /// </summary>
    public const string CountdownLabel = "履歴リセットまで:";

    /// <summary>カウントダウンが止まっている間に、残り時間の前に添える文字（2026-09-27のユーザー指定→実装メモ5.73）。</summary>
    public const string CountdownStoppedLabel = "カウントダウン停止中:";

    /// <summary>残り時間の前に添える文字。カウントダウンが止まっている間は <see cref="CountdownStoppedLabel"/>。</summary>
    public static string CountdownLabelFor(bool stopped) => stopped ? CountdownStoppedLabel : CountdownLabel;

    /// <summary>最後に描いた見出しで、残り時間の前に添えた文字（→<see cref="CountdownStopped"/>）。</summary>
    public string CurrentCountdownLabel => CountdownLabelFor(CountdownStopped);

    /// <summary>ボタンの文字の左右に取る余白。縦の余白は取らない（高さはカウントダウンに合わせるため）。</summary>
    private const float ResetButtonPaddingX = 14f;

    /// <summary>ボタンとカウントダウンの間隔。</summary>
    private const float ResetButtonGap = 14f;

    /// <summary>「延長」と「リセット」の間隔。</summary>
    private const float ClearButtonGap = 10f;

    /// <summary>「履歴リセットまで:」と数字の間隔。コロンの後ろに1文字ぶんの空きを置く。</summary>
    private const float CountdownLabelGap = 8f;

    /// <summary>ボタンの枠線の太さ。手首の距離で辺が消えないよう2pxにする。</summary>
    private const float ResetButtonBorder = 2f;

    /// <summary>
    /// カウントダウンの1フレームの高さ（px）。ボタンの高さもこれに合わせる。
    /// </summary>
    public int CountdownFrameHeight => (int)MathF.Ceiling(FontHeight(_fonts.Countdown)) + 2;

    /// <summary>
    /// 見出しのボタン（「延長」「リセット」）の大きさ（px）。2つとも同じ幅にする（2026-09-27のユーザー指定→実装メモ5.66）。
    /// 幅は長いほうの文字を測って決める。高さはカウントダウンと同じにして、見出しの中で2つの縦幅が揃って見えるようにする
    /// （2026-09-19のユーザー指定）。文字が入り切らないときだけ高さを広げる。
    /// 文字もフォントも変わらないので、1回だけ測る。
    /// </summary>
    public SizeF ButtonSize()
    {
        if (_buttonSize is { } size)
            return size;

        var text = MathF.Max(MeasureWidth(ResetButtonLabel, _fonts.ResetLabel), MeasureWidth(ClearButtonLabel, _fonts.ResetLabel));
        var width = text + (ResetButtonPaddingX * 2f);
        var label = FontHeight(_fonts.ResetLabel) + (ResetButtonBorder * 2f);
        var height = MathF.Max(CountdownFrameHeight, label);

        size = new SizeF(MathF.Ceiling(width), MathF.Ceiling(height));
        _buttonSize = size;
        return size;
    }

    /// <summary>
    /// 「リセット」のボタンを置く矩形（パネル内のpx座標）。見出しの右端へ、縦中央に置く（→実装メモ5.65）。
    /// </summary>
    public RectangleF ClearButtonRect()
    {
        var size = ButtonSize();

        return new RectangleF(
            Width - _style.PaddingRight - _style.ScrollBarWidth - size.Width,
            (_style.HeaderHeight - size.Height) / 2f,
            size.Width,
            size.Height);
    }

    /// <summary>
    /// 「延長」のボタンを置く矩形（パネル内のpx座標）。「リセット」のすぐ左へ、縦中央に置く。
    /// 2026-09-25のユーザー指定で残り時間と左右を入れ替え、2026-09-27に右端を「リセット」へ譲った（→実装メモ5.65）。
    /// </summary>
    public RectangleF ResetButtonRect()
    {
        var size = ButtonSize();

        return new RectangleF(
            ClearButtonRect().X - ClearButtonGap - size.Width,
            (_style.HeaderHeight - size.Height) / 2f,
            size.Width,
            size.Height);
    }

    /// <summary>
    /// 見出しを <paramref name="header"/> の状態で出したときの、「延長」のボタンの矩形。
    /// 描くときと命中を見るときで同じ計算を使う。残り時間がない・自動リセットを無効にしている・カウントダウンが止まっている・
    /// 該当する履歴がない（→実装メモ5.131）なら空（押せない）。
    /// </summary>
    public RectangleF ResetButtonRectFor(in PanelDecorations header)
        => header.Countdown is null || header.AutoResetDisabled || ResetButtonDisabled(header) ? RectangleF.Empty : ResetButtonRect();

    /// <summary>「延長」を薄くして押せなくするか。カウントダウンが止まっている（→実装メモ5.73）か、該当する履歴がない（→実装メモ5.131）とき。</summary>
    private static bool ResetButtonDisabled(in PanelDecorations header) => header.CountdownStopped || header.HistoryEmpty;

    /// <summary>
    /// 見出しに出す残り時間の文字。該当する履歴がない間は、数字を `-` にした <c>--:--</c>（→<see cref="Core.Presentation.Countdown.Blank"/>・実装メモ5.131）。
    /// 残り時間がなければ null。
    /// </summary>
    public static string? CountdownTextFor(in PanelDecorations header)
        => header.Countdown is { } text && header.HistoryEmpty ? Core.Presentation.Countdown.Blank(text) : header.Countdown;

    /// <summary>見出しを <paramref name="header"/> の状態で出したときの、「リセット」のボタンの矩形。残り時間がなければ空。</summary>
    public RectangleF ClearButtonRectFor(in PanelDecorations header)
        => header.Countdown is null ? RectangleF.Empty : ClearButtonRect();

    /// <summary>残り時間 <paramref name="countdown"/> を、最後に描いた見出しの状態（<see cref="AutoResetEnabled"/>・<see cref="CountdownStopped"/>）で出したときの「延長」の矩形。</summary>
    public RectangleF ResetButtonRectFor(string? countdown) => ResetButtonRectFor(LastHeader(countdown));

    /// <summary>残り時間 <paramref name="countdown"/> を出したときの、「リセット」のボタンの矩形。null なら空。</summary>
    public RectangleF ClearButtonRectFor(string? countdown) => ClearButtonRectFor(LastHeader(countdown));

    /// <summary>残り時間の文字と、最後に描いた見出しの状態を合わせたもの。</summary>
    private PanelDecorations LastHeader(string? countdown)
        => new() { Countdown = countdown, AutoResetDisabled = !AutoResetEnabled, CountdownStopped = CountdownStopped, HistoryEmpty = HistoryEmpty };

    /// <summary>見出し右の残り時間（数字）を置く矩形。ボタンのすぐ左へ、見出しの縦中央に置く。</summary>
    public RectangleF CountdownRect(float width, float height)
        => new(
            ResetButtonRect().X - ResetButtonGap - width,
            (_style.HeaderHeight - height) / 2f,
            width,
            height);

    /// <summary>
    /// 残り時間を出す矩形（パネル内のpx座標）。描くときと検証で同じ計算を使う。
    /// 残り時間がない・自動リセットを無効にしているなら空。
    /// </summary>
    public RectangleF CountdownRectFor(in PanelDecorations header)
        => header.Countdown is null || header.AutoResetDisabled
            ? RectangleF.Empty
            : CountdownRect(MeasureWidth(CountdownTextFor(header)!, _fonts.Countdown), CountdownFrameHeight);

    /// <summary>残り時間 <paramref name="countdown"/> を、最後に描いた見出しの状態で出す矩形。null なら空。</summary>
    public RectangleF CountdownRectFor(string? countdown) => CountdownRectFor(LastHeader(countdown));

    /// <summary>
    /// 残り時間の前に添える「履歴リセットまで:」の矩形。残り時間がない・自動リセットを無効にしているなら空。
    ///
    /// 数字は等幅なので、値が変わってもこの位置は動かない。文字は日本語のフォントで描き、
    /// 数字（等幅・一回り大きい）と<b>ベースラインをそろえる</b>。上端や中央でそろえると、
    /// 大きさの違う2つのフォントの字面が上下にずれて見えるため。
    /// </summary>
    public RectangleF CountdownLabelRectFor(in PanelDecorations header)
    {
        if (header.Countdown is null || header.AutoResetDisabled)
            return RectangleF.Empty;

        var digits = CountdownRectFor(header);
        var font = _fonts.Aux;
        var width = MeasureWidth(CountdownLabelFor(header.CountdownStopped), font);

        // 数字は枠の1px下から描いている（DrawHeader）。そのベースラインへ、文字のベースラインを合わせる。
        var baseline = digits.Y + 1f + Ascent(_fonts.Countdown);

        return new RectangleF(
            digits.X - CountdownLabelGap - width,
            baseline - Ascent(font),
            width,
            FontHeight(font));
    }

    /// <summary>残り時間 <paramref name="countdown"/> を、最後に描いた見出しの状態で出したときの「履歴リセットまで:」の矩形。null なら空。</summary>
    public RectangleF CountdownLabelRectFor(string? countdown) => CountdownLabelRectFor(LastHeader(countdown));

    /// <summary>自動リセットを無効にしている間の断りの矩形（「リセット」のすぐ左・縦中央）。</summary>
    public RectangleF AutoResetDisabledRect()
    {
        var clear = ClearButtonRect();
        var width = MeasureWidth(AutoResetDisabledLabel, _fonts.Absence);
        var height = FontHeight(_fonts.Absence);

        return new RectangleF(clear.X - ResetButtonGap - width, (_style.HeaderHeight - height) / 2f, width, height);
    }

    /// <summary>
    /// 見出しのボタン1つぶんの見た目。指している間は枠と文字をアクセント色にして、薄く塗る。
    /// <paramref name="danger"/>（「リセット」）は枠と文字を赤にし、指している間は赤で薄く塗る。
    /// </summary>
    private void DrawHeaderButton(Graphics graphics, RectangleF rect, string label, bool pointed, bool danger = false, bool disabled = false)
    {
        if (pointed)
        {
            // 見出しの帯の上に重ねるので、塗りは「指していることが分かる」程度に薄くする。
            graphics.FillRectangle(BrushFor(danger ? Color.FromArgb(64, _style.ClearButton) : Color.FromArgb(_style.BackgroundAlpha, _style.CurrentRow)), rect);
        }

        // 押せないときは薄くする（→実装メモ5.73）。
        var color = disabled ? Color.FromArgb(110, _style.Muted) : danger ? _style.ClearButton : pointed ? _style.Accent : _style.Muted;

        // 枠線はペンではなく塗りつぶしで描く（見出しの左端・上端でも欠けない→FillBorder）。
        var brush = BrushFor(color);
        FillBorder(graphics, brush, rect, ResetButtonBorder);

        var textWidth = MeasureWidth(label, _fonts.ResetLabel);
        var textHeight = FontHeight(_fonts.ResetLabel);

        graphics.DrawString(
            label,
            _fonts.ResetLabel,
            brush,
            rect.X + ((rect.Width - textWidth) / 2f),
            rect.Y + ((rect.Height - textHeight) / 2f),
            _format);
    }

    /// <summary>
    /// 見出し左に出す表題。件数（2026-09-19のユーザー指定で下部右から移した）と、
    /// 先頭の行（いちばん古い訪問）へ入った時刻（2026-09-25のユーザー指定）を入れる。
    /// 履歴がいつからのものかが、スクロールして先頭を見に行かなくても分かる。
    /// </summary>
    /// <param name="since">先頭の行へ入った時刻（<c>HH:mm</c>）。行がなければ null で、時刻を出さない。</param>
    /// <param name="showCount">件数（<c>全n件</c>）を出すか。履歴の自動リセットを無効にしている間は出さない（→実装メモ5.109）。</param>
    public static string HeaderTitle(int rowCount, string? since = null, bool showCount = true)
        => (showCount, string.IsNullOrEmpty(since)) switch
        {
            (true, true) => string.Create(CultureInfo.InvariantCulture, $"訪問履歴 (全{rowCount}件)"),
            (true, false) => string.Create(CultureInfo.InvariantCulture, $"訪問履歴 (全{rowCount}件 {since}~)"),
            (false, true) => "訪問履歴",
            (false, false) => $"訪問履歴 ({since}~)",
        };

    /// <summary>
    /// 見出し左に出す表題。行の下絵を描いたときの行数と先頭の行から決まる。
    /// 履歴の自動リセットを無効にしている間は、何日も前の行が残るので日付まで出し（→実装メモ5.71）、件数は出さない（2026-10-01のユーザー指定→実装メモ5.109）。
    /// </summary>
    private string HeaderTitleFor(bool autoResetEnabled)
        => HeaderTitle(_rowCount, autoResetEnabled ? _since : _sinceWithDate, showCount: autoResetEnabled);

    /// <summary>最後に描いた見出しの表題（→<see cref="AutoResetEnabled"/>）。</summary>
    public string HeaderTitleText => HeaderTitleFor(AutoResetEnabled);

    /// <summary>履歴の自動リセットを無効にしている間、見出しの右に出す文字（→実装メモ5.71）。</summary>
    public const string AutoResetDisabledLabel = "履歴の自動リセットが無効化されています";

    /// <summary>
    /// 表題の矩形（パネル内のpx座標）。<paramref name="title"/> を省くと、いま描いている表題のもの。
    /// 見出し右の文字と重ならないことの検証にも使う。
    /// </summary>
    public RectangleF HeaderTitleRect(string? title = null)
    {
        var height = FontHeight(_fonts.Title);

        return new RectangleF(
            _style.PaddingLeft,
            (_style.HeaderHeight - height) / 2f,
            MeasureWidth(title ?? HeaderTitleText, _fonts.Title),
            height);
    }

    /// <summary>見出しを描く。右側の状態（残り時間・自動リセット・停止中）は <paramref name="header"/> の値だけで決める。</summary>
    private void DrawHeader(in PanelDecorations header)
    {
        var muted = BrushFor(_style.Muted);
        var autoResetEnabled = !header.AutoResetDisabled;

        var titleText = HeaderTitleFor(autoResetEnabled);
        var title = HeaderTitleRect(titleText);
        _graphics.DrawString(titleText, _fonts.Title, BrushFor(_style.Text), title.X, title.Y, _format);

        // 残り時間が決まっていない間は、右側に何も入れない。
        if (header.Countdown is not { } note)
            return;

        // 右端から「リセット」「延長」、残り時間、「履歴リセットまで:」の順に並べる（2026-09-25・2026-09-27のユーザー指定）。
        // カウントダウンが止まっている間・該当する履歴がない間は「延長」だけを薄くして押せなくする
        // （「履歴リセット」はいつでも押せる→実装メモ5.73・5.74・5.131）。
        var disabled = ResetButtonDisabled(header);
        DrawHeaderButton(_graphics, ClearButtonRect(), ClearButtonLabel, header.ClearPointed, danger: true);

        // 自動リセットを無効にしている間は、残り時間と「延長」の代わりに断りを出す（→実装メモ5.71）。
        // 手で消す「リセット」は残す（無効にしている間こそ使うため）。
        if (!autoResetEnabled)
        {
            var notice = AutoResetDisabledRect();
            _graphics.DrawString(AutoResetDisabledLabel, _fonts.Absence, muted, notice.X, notice.Y, _format);
            return;
        }

        DrawHeaderButton(_graphics, ResetButtonRect(), ResetButtonLabel, header.ResetPointed && !disabled, disabled: disabled);

        var countdown = CountdownRectFor(header);
        DrawCountdownDigits(CountdownTextFor(header) ?? note, countdown.X, countdown.Y + 1f, header.CountdownGlow, header.CountdownWarning);

        var label = CountdownLabelRectFor(header);
        _graphics.DrawString(CountdownLabelFor(header.CountdownStopped), _fonts.Aux, muted, label.X, label.Y, _format);
    }

    /// <summary>
    /// 発光の光を広げるペンの太さ（px）と、強さ1のときの不透明度。外側ほど太く薄くして、ぼかしの代わりにする。
    /// いちばん外の光（太さの半分＝8px）が、左の「履歴リセットまで:」との間隔（<see cref="CountdownLabelGap"/>）に収まる太さにする。
    /// </summary>
    private static readonly (float Width, int Alpha)[] CountdownGlowLayers = [(16f, 18), (12f, 28), (8f, 44), (4f, 76)];

    /// <summary>
    /// 発光の量。強さ（<see cref="PanelDecorations.CountdownGlow"/>）にこれを掛けて、光の不透明度と数字の明るくなり方を決める
    /// （2026-10-05のユーザー指定で、初めの量の2/3にした）。
    /// </summary>
    private const float CountdownGlowAmount = 2f / 3f;

    /// <summary>発光の強さ1のときの数字の色の、アクセント色に混ぜる白の割合。光の中心ほど白く見えるようにする。</summary>
    private const float CountdownGlowWhiten = 0.55f;

    /// <summary>
    /// 残り時間の数字を描く（→実装メモ5.130）。
    /// <paramref name="warning"/>（0〜1）で通常の色から赤みがかった色へ寄せ、<paramref name="glow"/>（0〜1）で
    /// 周りに光を広げて数字を明るくする。どちらも0なら、これまでどおり通常の色で描くだけ。
    /// </summary>
    private void DrawCountdownDigits(string text, float x, float y, float glow, float warning)
    {
        glow *= CountdownGlowAmount;

        var font = _fonts.Countdown;
        var color = Mix(_style.Muted, _style.CountdownWarning, warning);

        if (glow > 0f)
        {
            // GDI+ にはぼかしがないので、文字の輪郭を太さの違うペンで重ね塗りして光に見せる。
            // 見出しの帯の外（行の側）へはみ出さないよう、見出しの中に切る。
            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, font.Size, new PointF(x, y), _format);

            var state = _graphics.Save();
            _graphics.SetClip(new RectangleF(0f, 0f, Width, _style.HeaderHeight));
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (var (width, alpha) in CountdownGlowLayers)
            {
                using var pen = new Pen(Color.FromArgb((int)MathF.Round(alpha * glow), _style.CountdownGlow), width) { LineJoin = LineJoin.Round };
                _graphics.DrawPath(pen, path);
            }

            _graphics.Restore(state);

            color = Mix(color, Mix(_style.CountdownGlow, Color.White, CountdownGlowWhiten), glow);
        }

        _graphics.DrawString(text, font, BrushFor(color), x, y, _format);
    }

    /// <summary><paramref name="from"/> から <paramref name="to"/> へ <paramref name="amount"/>（0〜1）だけ寄せた色（不透明）。</summary>
    private static Color Mix(Color from, Color to, float amount)
    {
        if (amount <= 0f)
            return from;

        if (amount >= 1f)
            return to;

        static int Channel(int a, int b, float t) => (int)MathF.Round(a + ((b - a) * t));

        return Color.FromArgb(Channel(from.R, to.R, amount), Channel(from.G, to.G, amount), Channel(from.B, to.B, amount));
    }
}
