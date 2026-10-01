using System.Drawing;
using System.Drawing.Drawing2D;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Desktop;

/// <summary>設定の部品の描き方（<see cref="Render"/>）。</summary>
public sealed partial class SettingsView
{
    /// <summary>使えない部品に掛ける覆いの不透明度（→実装メモ5.71）。</summary>
    private const int VeilAlpha = 160;

    public void Render(Graphics graphics)
    {
        foreach (var section in _sections)
            DrawSection(graphics, section);

        foreach (var (rect, text) in _labels)
            DrawLabel(graphics, rect, text);

        var muted = Painter.Brush(_style.Muted);

        foreach (var (origin, text) in _intros)
            graphics.DrawString(text, Painter.Fonts.Absence, muted, origin.X, origin.Y + S(1f), Painter.Format);

        foreach (var target in _targets)
            DrawTarget(graphics, target);

        // 使えない部品は薄くする（→実装メモ5.71）。VRオーバーレイ機能をオフにしている間は手首パネルの枠ごと、
        // 自動リセットを無効にしている間はリセットまでの時間の行だけ。
        var veil = Painter.Brush(Color.FromArgb(VeilAlpha, _style.Surface));

        if (!_settings.VrOverlayEnabled && !_wristSection.IsEmpty)
            graphics.FillRectangle(veil, _wristSection);

        if (!_settings.AutoResetEnabled && _stepperRows.TryGetValue(SettingsStepper.Retention, out var retentionRow))
            graphics.FillRectangle(veil, retentionRow);

        // 予告通知は、VRオーバーレイ機能をオフにしている間は枠ごと、「リセット予告アイコンを表示する」をオフにしている間はその下だけ（→実装メモ5.89）。
        if (!_settings.VrOverlayEnabled && !_warningSection.IsEmpty)
            graphics.FillRectangle(veil, _warningSection);
        else if (!_settings.ResetWarningEnabled && !_warningBody.IsEmpty)
            graphics.FillRectangle(veil, _warningBody);

        // 吹き出しはいちばん手前。
        DrawHint(graphics);

        Dirty = false;
    }

    private void DrawTarget(Graphics graphics, HitTarget target)
    {
        if (SettingsChecks.TryGet(target.Kind, out var check))
        {
            DrawCheck(graphics, target, check.Label, check.Get(_settings), enabled: !check.GreysOut || IsEnabled(target));
            return;
        }

        switch (target.Kind)
        {
            // −／＋ は1行で1つ。− の番で行ごと描く。
            case HitKind.StepperMinus:
                DrawStepper(graphics, target);
                break;

            case HitKind.TargetType:
            {
                var type = TargetAccessTypes.Selectable[target.Index];
                DrawCheck(graphics, target, type.DisplayName(), _settings.TargetTypes.Contains(type), painter: Emphasis);
                break;
            }

            case HitKind.ResetPlacement:
                Painter.DrawButton(graphics, target.Rect, "手首パネルの位置をデフォルトに戻す", IsPointed(target));
                break;

            case HitKind.ResetWristParameters:
                Painter.DrawButton(graphics, target.Rect, "手首パネルのパラメータをデフォルトに戻す", IsPointed(target));
                break;

            case HitKind.UndoClear:
                Painter.DrawButton(graphics, target.Rect, "直前のリセットを戻す", IsPointed(target), IsEnabled(target));
                break;

            case HitKind.GroupImport:
                Painter.DrawButton(graphics, target.Rect, "インポート", IsPointed(target));
                break;

            case HitKind.GroupExport:
                Painter.DrawButton(graphics, target.Rect, "エクスポート", IsPointed(target));
                break;

            case HitKind.CheckUpdates:
                Painter.DrawButton(graphics, target.Rect, "今すぐ確認", IsPointed(target), IsEnabled(target));
                DrawUpdateStatus(graphics);
                break;

            case HitKind.ApplyUpdate:
                Painter.DrawButton(graphics, target.Rect, "更新して再起動", IsPointed(target), IsEnabled(target));
                break;

            case HitKind.WristSide:
                DrawSegment(graphics, target, WristChoices[target.Index].Label, WristChoices[target.Index].Value == _settings.Wrist);
                break;

            case HitKind.ReturnAction:
                DrawSegment(graphics, target, ReturnChoices[target.Index].Label, ReturnChoices[target.Index].Value == _settings.ReturnAction);
                break;

            case HitKind.PhotoViewer:
                DrawSegment(graphics, target, PhotoViewers.DisplayName(ViewerChoices[target.Index]), ViewerChoices[target.Index] == _settings.PhotoViewer);
                break;

            case HitKind.CopyCommand:
                DrawCommand(graphics, target);
                break;
        }
    }

    /// <summary>設定のまとまり1つ。パネルと同じく、見出しの帯（Header）と本文（Surface）の2段で描く。</summary>
    private void DrawSection(Graphics graphics, Section section)
    {
        var rect = section.Rect;
        var fonts = Painter.Fonts;
        var format = Painter.Format;

        Painter.DrawCard(graphics, rect, S(SectionTitleHeight));
        graphics.DrawString(section.Title, fonts.Title, Painter.Brush(_style.Text), rect.X + S(SectionPadding), rect.Y + ((S(SectionTitleHeight) - fonts.Title.GetHeight(graphics)) / 2f), format);

        var muted = Painter.Brush(_style.Muted);

        for (var i = 0; i < section.Notes.Count; i++)
            graphics.DrawString(section.Notes[i], fonts.Absence, muted, rect.X + S(SectionPadding), section.NotesTop + (i * S(NoteLineHeight)) + S(1f), format);
    }

    /// <summary>選択肢の行の名前（−／＋ の行の名前と同じ書き方）。</summary>
    private void DrawLabel(Graphics graphics, RectangleF rect, string text)
    {
        var font = Painter.Fonts.Aux;
        graphics.DrawString(text, font, Painter.Brush(_style.Text), rect.X, rect.Y + ((rect.Height - font.GetHeight(graphics)) / 2f), Painter.Format);
    }

    private void DrawStepper(Graphics graphics, HitTarget minus)
    {
        var id = minus.Stepper;
        var spec = SettingsSteppers.Spec(id);
        var plus = _targets.First(t => t.Kind == HitKind.StepperPlus && t.Index == minus.Index);
        var row = _stepperRows[id];
        var value = spec.Get(_settings);
        var format = Painter.Format;

        // 目立たせるものは EmphasisScale 倍の字（→実装メモ5.106）。
        var labelFont = spec.Prominent ? Emphasis.ProminentLabel : Painter.Fonts.Aux;
        var valueFont = spec.Prominent ? Emphasis.ProminentValue : Painter.Fonts.AuxMono;

        graphics.DrawString(spec.Label, labelFont, Painter.Brush(_style.Text), row.X + S(SectionPadding), row.Y + ((row.Height - labelFont.GetHeight(graphics)) / 2f), format);

        // 値は −／＋ の間の中央に置く。等幅にして桁が変わっても揺れないようにする。
        // 自動リセットを無効にしている間のリセットまでの時間は「-- 分」（→実装メモ5.71）。
        var valueText = id == SettingsStepper.Retention && !_settings.AutoResetEnabled ? "-- 分" : spec.Format(value);
        var valueY = row.Y + ((row.Height - valueFont.GetHeight(graphics)) / 2f);
        var valueBrush = Painter.Brush(spec.Prominent ? _style.Accent : _style.Text);

        // 目立たせる値は、数字と単位（「分」）を分け、単位を数字の0.6倍の字で下（ベースライン）をそろえて添える（→実装メモ5.69）。
        if (spec.Prominent && valueText.LastIndexOf(' ') is var space and > 0)
        {
            var number = valueText[..space];
            var unit = valueText[(space + 1)..];
            var unitFont = Emphasis.ProminentUnit;
            var gap = E(3f);
            var numberWidth = Painter.MeasureWidth(number, valueFont);
            var total = numberWidth + gap + Painter.MeasureWidth(unit, unitFont);
            var left = minus.Rect.Right + ((plus.Rect.X - minus.Rect.Right - total) / 2f);
            var unitY = valueY + UiPainter.Ascent(valueFont) - UiPainter.Ascent(unitFont);

            graphics.DrawString(number, valueFont, valueBrush, left, valueY, format);
            graphics.DrawString(unit, unitFont, valueBrush, left + numberWidth + gap, unitY, format);
        }
        else
        {
            var valueWidth = Painter.MeasureWidth(valueText, valueFont);
            var valueLeft = minus.Rect.Right + ((plus.Rect.X - minus.Rect.Right - valueWidth) / 2f);
            graphics.DrawString(valueText, valueFont, valueBrush, valueLeft, valueY, format);
        }

        var enabled = StepperEnabled(id);

        // 巡る部品（表示位置）は端がないので、‹ › をいつでも押せる（→実装メモ5.89）。
        if (spec.Cycle)
        {
            DrawStepButton(graphics, minus, plusSign: false, enabled: enabled, chevron: true);
            DrawStepButton(graphics, plus, plusSign: true, enabled: enabled, chevron: true);
            return;
        }

        DrawStepButton(graphics, minus, plusSign: false, enabled: enabled && value > spec.Min);
        DrawStepButton(graphics, plus, plusSign: true, enabled: enabled && value < spec.Max);
    }

    private void DrawStepButton(Graphics graphics, HitTarget target, bool plusSign, bool enabled, bool chevron = false)
    {
        var pointed = enabled && IsPointed(target);
        var pressed = enabled && _pressed is { } p && p.Kind == target.Kind && p.Index == target.Index;
        var color = !enabled ? _style.Rule : pointed ? _style.Accent : _style.Muted;

        // 目立たせるものは、枠と記号の線も EmphasisScale 倍の太さ（→実装メモ5.106）。
        var painter = SettingsSteppers.Spec(target.Stepper).Prominent ? Emphasis : Painter;

        if (pointed || pressed)
            graphics.FillRectangle(Painter.Brush(_style.CurrentRow), target.Rect);

        painter.DrawBorder(graphics, target.Rect, color);

        // 記号は文字ではなく線で描く（フォントによって − と + の太さや位置が揃わないため）。
        var center = new PointF(target.Rect.X + (target.Rect.Width / 2f), target.Rect.Y + (target.Rect.Height / 2f));
        var half = target.Rect.Width * (5f / StepButtonSize);
        var thickness = MathF.Max(1f, MathF.Round(painter.S(1.5f)));

        if (chevron)
        {
            // ‹ ›。plusSign が右向き（次へ）。
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            var dx = half * 0.55f * (plusSign ? 1f : -1f);
            graphics.DrawLines(pen,
            [
                new PointF(center.X - dx, center.Y - half),
                new PointF(center.X + dx, center.Y),
                new PointF(center.X - dx, center.Y + half),
            ]);

            graphics.Restore(state);
            return;
        }

        var brush = Painter.Brush(color);
        graphics.FillRectangle(brush, center.X - half, center.Y - (thickness / 2f), half * 2f, thickness);

        if (plusSign)
            graphics.FillRectangle(brush, center.X - (thickness / 2f), center.Y - half, thickness, half * 2f);
    }

    /// <summary>
    /// 選択肢の1つ（→実装メモ5.44・5.49）。いま選んでいるものはアクセント色の枠と薄い塗り、
    /// 指しているものはアクセント色の枠にする。
    /// </summary>
    private void DrawSegment(Graphics graphics, HitTarget target, string label, bool selected)
    {
        var pointed = IsPointed(target);

        if (selected || pointed)
            graphics.FillRectangle(Painter.Brush(_style.CurrentRow), target.Rect);

        Painter.DrawBorder(graphics, target.Rect, selected || pointed ? _style.Accent : _style.Muted);

        var font = Painter.Fonts.ResetLabel;
        var width = Painter.MeasureWidth(label, font);
        var brush = Painter.Brush(selected ? _style.Accent : pointed ? _style.Text : _style.Muted);
        graphics.DrawString(label, font, brush, target.Rect.X + ((target.Rect.Width - width) / 2f), target.Rect.Y + ((target.Rect.Height - font.GetHeight(graphics)) / 2f), Painter.Format);
    }

    /// <param name="painter">箱・線・字の大きさを決める描き手。省くと <see cref="Painter"/>（インスタンスタイプは <see cref="Emphasis"/>→実装メモ5.106）。</param>
    private void DrawCheck(Graphics graphics, HitTarget target, string label, bool on, bool enabled = true, UiPainter? painter = null)
    {
        var p = painter ?? Painter;
        var pointed = enabled && IsPointed(target);
        var size = p.S(CheckBoxSize);
        var box = new RectangleF(target.Rect.X, target.Rect.Y + ((target.Rect.Height - size) / 2f), size, size);

        if (on && enabled)
        {
            graphics.FillRectangle(Painter.Brush(_style.Accent), box);

            // チェックの印は背景色で抜く。
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(_style.Surface, MathF.Max(1.5f, p.S(2f)));
            graphics.DrawLines(pen,
            [
                new PointF(box.X + (size * 0.22f), box.Y + (size * 0.52f)),
                new PointF(box.X + (size * 0.42f), box.Y + (size * 0.72f)),
                new PointF(box.X + (size * 0.78f), box.Y + (size * 0.30f)),
            ]);

            graphics.Restore(state);
        }
        else
        {
            p.DrawBorder(graphics, box, !enabled ? _style.Rule : pointed ? _style.Accent : _style.Muted);
        }

        var font = p.Fonts.Aux;
        var color = !enabled ? _style.Rule : on || pointed ? _style.Text : _style.Muted;
        graphics.DrawString(label, font, Painter.Brush(color), box.Right + p.S(8f), target.Rect.Y + ((target.Rect.Height - font.GetHeight(graphics)) / 2f), Painter.Format);
    }

    /// <summary>
    /// 押せない部品を指している間の吹き出し。部品の行のすぐ上（2026-09-27に下から移した→実装メモ5.70）、チェックの箱の右に置き、ほかの部品の上に重ねる。
    /// ポインターに付いて動かさない（動かすたびに描き直さないため）。
    /// </summary>
    private void DrawHint(Graphics graphics)
    {
        if (_hoveredTarget is not { } target || DisabledHint(target) is not { } text)
            return;

        var font = Painter.Fonts.Absence;
        var padX = S(8f);
        var padY = S(4f);
        var width = Painter.MeasureWidth(text, font) + (padX * 2f);
        var height = font.GetHeight(graphics) + (padY * 2f);
        var x = MathF.Min(target.Rect.X + S(CheckBoxSize) + S(8f), Bounds.Right - width);
        var rect = new RectangleF(x, target.Rect.Y - height - S(2f), width, height);

        graphics.FillRectangle(Painter.Brush(_style.Header), rect);
        Painter.DrawBorder(graphics, rect, _style.Muted);
        graphics.DrawString(text, font, Painter.Brush(_style.Text), rect.X + padX, rect.Y + padY, Painter.Format);
    }

    /// <summary>
    /// アップデートのまとまりの1・2行目（現在のバージョンと状態→実装メモ5.121・5.122）。1行目は通常の色、2行目は新バージョンがあればアクセント色（→実装メモ5.123）。
    /// 入り切らなければ、省略記号で切らずに末尾を薄くする。
    /// </summary>
    private void DrawUpdateStatus(Graphics graphics)
    {
        if (_updateStatusRow.IsEmpty)
            return;

        var status = UpdateOf(_settings);
        var font = Painter.Fonts.Aux;
        var line = _updateStatusRow.Height / 2f;
        var offset = (line - font.GetHeight(graphics)) / 2f;

        Painter.DrawFadingText(graphics, UpdateVersionText, font, _style.Text, _updateStatusRow.X, _updateStatusRow.Y + offset, _updateStatusRow.Width);
        // 新バージョンが公開されているときはアクセント色（2026-10-01のユーザー指定→実装メモ5.123）、確認に失敗したときは赤にする（→実装メモ5.124）。
        var color = status.Phase == UpdatePhase.Failed ? _style.Crash : status.Offering ? _style.Accent : _style.Text;
        Painter.DrawFadingText(graphics, UpdateStatusText(status), font, color, _updateStatusRow.X, _updateStatusRow.Y + line + offset, _updateStatusRow.Width);
    }

    /// <summary>
    /// 外部連携のコマンドの枠（等幅の字・右端に「コピー」）と、その下の「最終実行」の段。
    /// コマンドは切らずに見せたいので、枠に入り切らなければ字を小さくする。
    /// </summary>
    private void DrawCommand(Graphics graphics, HitTarget copy)
    {
        graphics.FillRectangle(Painter.Brush(_style.Header), _codeBlock);
        Painter.DrawBorder(graphics, _codeBlock, _style.Rule);

        var command = ExternalCommandListener.CommandLine();
        var room = copy.Rect.X - _codeBlock.X - S(CodePadding * 2f);
        var codeFont = Painter.FittedFont(command, Painter.Fonts.AuxMono, room);

        graphics.DrawString(command, codeFont, Painter.Brush(_style.Text), _codeBlock.X + S(CodePadding), _codeBlock.Y + ((_codeBlock.Height - codeFont.GetHeight(graphics)) / 2f), Painter.Format);

        Painter.DrawButton(graphics, copy.Rect, _copied ? "コピー済み" : "コピー", IsPointed(copy) || _copied);

        var labelFont = Painter.Fonts.Aux;
        graphics.DrawString(LastRunText(_settings.ExternalResetLastRunUtc), labelFont, Painter.Brush(_style.Muted), _lastRunRow.X, _lastRunRow.Y + ((_lastRunRow.Height - labelFont.GetHeight(graphics)) / 2f), Painter.Format);
    }
}
