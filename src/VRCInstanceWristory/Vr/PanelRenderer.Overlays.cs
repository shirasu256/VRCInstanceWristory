using System.Drawing;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 行の上に重ねるもの。指している行の帯・デスクトップのウィンドウで選んだ行の枠・目印のポップアップ（→実装メモ5.32・5.43）と、
/// 履歴リセットの確認（→実装メモ5.65）。ポップアップの部品はデスクトップのウィンドウも自分の面へ描くのに使う。
/// </summary>
public sealed partial class PanelRenderer
{
    private void DrawPopup(in PanelDecorations decorations)
    {
        DrawMarkPopup(_graphics, decorations.Popup);

        for (var i = 0; i < Core.Marks.InstanceMarks.Choices.Count; i++)
        {
            var choice = Core.Marks.InstanceMarks.Choices[i];

            DrawMarkChoice(
                _graphics,
                PanelGeometry.MarkChoiceRect(decorations.Popup, i),
                choice,
                pointed: choice == decorations.PopupPointed,
                selected: choice == decorations.PopupCurrent);
        }

        DrawReturnChoice(_graphics, decorations.Popup, decorations.PopupReturnAction, decorations.PopupReturnEnabled, decorations.PopupReturnPointed);
    }
    /// <summary>行を「わずかに白く」する塗り。指している行の上に重ねる。</summary>
    public void DrawRowHighlight(Graphics graphics, RectangleF rect)
    {
        graphics.FillRectangle(BrushFor(Color.FromArgb(_style.RowHighlightAlpha, _style.RowHighlight)), rect);
    }

    /// <summary>選んでいる行の枠（2px・アクセント色）。縁でも欠けないよう塗りつぶしで描く（→5.18）。</summary>
    public void DrawRowSelection(Graphics graphics, RectangleF rect)
    {
        const float border = 2f;

        FillBorder(graphics, BrushFor(_style.Accent), rect, border);
    }
    /// <summary>いま滞在しているインスタンスの行では「ここへ戻る」の代わりにこれを出し、押せないようにする。</summary>
    public const string ReturnHereLabel = "滞在中";

    /// <summary>場所から URL を作れない行（ログの location が読めない）では、ボタンの代わりにこれを出す。</summary>
    public const string CannotOpenLabel = "開けません";
    /// <summary>
    /// ポップアップの右端の「ブラウザで開く」／「ここへ戻る」（2026-09-26のユーザー指定→実装メモ5.43・5.53）。
    ///
    /// 目印の選択肢との間に区切りの線を引いて、「付け外し」と「開く」を見分けられるようにする。
    /// 指している間は目印と同じくアクセント色の枠と塗り。押せないとき（「ここへ戻る」で滞在中のインスタンス）は、
    /// 印と文字を控えめな色にして「滞在中」と出す。
    /// </summary>
    public void DrawReturnChoice(Graphics graphics, RectangleF popup, Core.Locations.ReturnAction action, bool enabled, bool pointed)
    {
        var rect = PanelGeometry.ReturnChoiceRect(popup);
        var border = PanelGeometry.MarkPopupBorder;

        // 区切りの線（選択肢と「ここへ戻る」の間の真ん中）。
        var ruleX = rect.X - (PanelGeometry.ReturnChoiceGap / 2f) - 1f;
        graphics.FillRectangle(BrushFor(_style.Rule), ruleX, rect.Y + 8f, 2f, rect.Height - 16f);

        pointed &= enabled;

        if (pointed)
        {
            graphics.FillRectangle(BrushFor(Color.FromArgb(_style.BackgroundAlpha, _style.CurrentRow)), rect);
            FillBorder(graphics, BrushFor(_style.Accent), rect, border);
        }

        // 押せないときは控えめな色にする（区切りの線の色まで落とすと、暗い下地の上で読めない）。
        var color = !enabled ? Color.FromArgb(150, _style.Muted) : pointed ? _style.Accent : _style.Text;
        var label = enabled
            ? Core.Locations.ReturnActions.ButtonLabel(action)
            : action == Core.Locations.ReturnAction.VrChat ? ReturnHereLabel : CannotOpenLabel;
        var font = _fonts.Aux;

        const float iconSize = 34f;
        const float gap = 10f;

        var textWidth = MeasureWidth(label, font);
        var total = iconSize + gap + textWidth;
        var x0 = rect.X + ((rect.Width - total) / 2f);

        var icon = new RectangleF(x0, rect.Y + ((rect.Height - iconSize) / 2f), iconSize, iconSize);

        if (action == Core.Locations.ReturnAction.VrChat)
            IconPainter.DrawReturn(graphics, icon, color);
        else
            IconPainter.DrawGlobe(graphics, icon, color);

        graphics.DrawString(label, font, BrushFor(color), x0 + iconSize + gap, rect.Y + ((rect.Height - font.GetHeight(graphics)) / 2f), _format);
    }

    /// <summary>
    /// ポップアップの下地。行の上に重ねるので、パネルの背景よりも濃くして下の文字を透かさない。
    /// 枠線は見出しのボタンと同じく塗りつぶしで描く（1px幅のペンは縁で半分消えるため→5.18）。
    /// </summary>
    public void DrawMarkPopup(Graphics graphics, RectangleF rect)
    {
        // 「ここへ戻る」を足して横に長くなり、下の行の文字に重なる幅が増えたので、透かさないようにした（→実装メモ5.43）。
        const int popupAlpha = 255;
        var border = PanelGeometry.MarkPopupBorder;

        graphics.FillRectangle(BrushFor(Color.FromArgb(popupAlpha, _style.Header)), rect);
        FillBorder(graphics, BrushFor(Color.FromArgb(popupAlpha, _style.Muted)), rect, border);
    }

    /// <summary>
    /// 選択肢1つの見た目。
    ///
    /// 指している間は枠と塗りをアクセント色にして、どれを選ぼうとしているかを示す。
    /// いま付いている目印（選択中）は、その印の色の枠と薄い塗りで示す。
    /// 選択中の印をもう一度選ぶと外れる（2026-09-22のユーザー指定）。
    /// </summary>
    public void DrawMarkChoice(Graphics graphics, RectangleF rect, Core.Marks.InstanceMark mark, bool pointed, bool selected)
    {
        var color = _style.MarkColor(mark);
        var border = PanelGeometry.MarkPopupBorder;

        if (pointed)
            graphics.FillRectangle(BrushFor(Color.FromArgb(_style.BackgroundAlpha, _style.CurrentRow)), rect);
        else if (selected)
            graphics.FillRectangle(BrushFor(Color.FromArgb(38, color)), rect);

        if (pointed || selected)
            FillBorder(graphics, BrushFor(pointed ? _style.Accent : color), rect, border);

        // 指しても選んでもいない選択肢は、印だけを少し控えめに出す。
        var glyph = pointed || selected ? color : Color.FromArgb(190, color);
        var inset = rect.Width * 0.23f;

        MarkPainter.Draw(graphics, mark, RectangleF.Inflate(rect, -inset, -inset), glyph);
    }
    /// <summary>履歴リセットの確認の、消すボタン（見出しのボタンは「履歴リセット」→実装メモ5.74）。</summary>
    public const string ConfirmAcceptLabel = "リセット";

    /// <summary>履歴リセットの確認の1行目（2026-09-27のユーザー指定→実装メモ5.65）。</summary>
    public const string ConfirmClearMessage = "すべての訪問履歴を今すぐリセットします。";

    /// <summary>履歴リセットの確認の2行目。</summary>
    public const string ConfirmClearQuestion = "よろしいですか？";

    /// <summary>履歴リセットの確認の、やめるボタン。</summary>
    public const string ConfirmCancelLabel = "キャンセル";

    /// <summary>
    /// 履歴リセットの確認（2026-09-27のユーザー指定→実装メモ5.65）。
    /// パネル全体を暗くしてから中央に枠を置き、左に文言、右に「キャンセル」「リセット」を並べる。
    /// </summary>
    private void DrawClearConfirm(Graphics graphics, int pointed)
    {
        // 下の行が透けて読めると何を問われているのか紛れるので、パネル全体を暗く沈める。
        graphics.FillRectangle(BrushFor(Color.FromArgb(150, 0, 0, 0)), 0, 0, Width, Height);

        var box = PanelGeometry.ConfirmBoxRect(_style, Height);
        var border = PanelGeometry.MarkPopupBorder;

        graphics.FillRectangle(BrushFor(_style.Header), box);

        // 枠は目印のポップアップと同じ標準の色（赤いのは「リセット」のボタンだけ→実装メモ5.69）。
        FillBorder(graphics, BrushFor(_style.Muted), box, border);

        var font = _fonts.Aux;
        var lineHeight = _style.AuxLineHeight + 4f;
        var textTop = box.Y + ((box.Height - (lineHeight * 2f)) / 2f) + ((lineHeight - font.GetHeight(graphics)) / 2f);

        var text = BrushFor(_style.Text);
        graphics.DrawString(ConfirmClearMessage, font, text, box.X + PanelGeometry.ConfirmPadding, textTop, _format);
        graphics.DrawString(ConfirmClearQuestion, font, text, box.X + PanelGeometry.ConfirmPadding, textTop + lineHeight, _format);

        DrawConfirmButton(graphics, PanelGeometry.ConfirmButtonRect(box, PanelGeometry.ConfirmCancel), ConfirmCancelLabel, pointed == PanelGeometry.ConfirmCancel, danger: false);
        DrawConfirmButton(graphics, PanelGeometry.ConfirmButtonRect(box, PanelGeometry.ConfirmAccept), ConfirmAcceptLabel, pointed == PanelGeometry.ConfirmAccept, danger: true);
    }

    /// <summary>確認のボタン1つ。「リセット」は赤で、指している間は赤く塗りつぶす（枠は縁でも欠けないよう塗りで描く→5.18）。</summary>
    private void DrawConfirmButton(Graphics graphics, RectangleF rect, string label, bool pointed, bool danger)
    {
        var border = PanelGeometry.MarkPopupBorder;
        var color = danger ? _style.Crash : pointed ? _style.Accent : _style.Muted;

        if (pointed)
            graphics.FillRectangle(BrushFor(danger ? _style.Crash : _style.CurrentRow), rect);

        FillBorder(graphics, BrushFor(color), rect, border);

        var font = _fonts.Aux;
        var textColor = danger ? (pointed ? _style.Header : _style.Crash) : pointed ? _style.Accent : _style.Text;

        graphics.DrawString(
            label,
            font,
            BrushFor(textColor),
            rect.X + ((rect.Width - MeasureWidth(label, font)) / 2f),
            rect.Y + ((rect.Height - font.GetHeight(graphics)) / 2f),
            _format);
    }
}
