using System.Drawing;
using VRCInstanceWristory.Core.Marks;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// パネルの上の操作。見出しの「延長」「履歴リセット」とその確認（→実装メモ5.65）、
/// 指している行の帯と目印のポップアップ（→実装メモ5.32・5.43）。どれもトリガーで押す。
/// </summary>
public sealed partial class OverlayController
{
    // 見出しの「延長」。
    private bool _resetPointed;
    private bool _resetPressed;
    private readonly GraceTimer _resetHold = new(HitGrace);

    // 見出しの「リセット」と、その確認（→実装メモ5.65）。
    private bool _clearPointed;
    private readonly GraceTimer _clearHold = new(HitGrace);
    private bool _confirmOpen;
    private int _confirmPointed = -1;
    private bool _clearPressed;

    // 指している行と、目印を選ぶポップアップ（→実装メモ5.32）。
    private PanelRenderer.RowHit _hoverRow;
    private bool _hasHoverRow;
    private bool _popupOpen;
    private string? _popupEventId;
    private InstanceMark _popupCurrent;
    private RectangleF _popupRect;
    private int _pointedChoice = -1;
    private bool _popupReturnEnabled;
    private (string EventId, InstanceMark Mark)? _markRequest;

    // 「ブラウザで開く」／「ここへ戻る」（→実装メモ5.43・5.53）。選ばれた行を預かり、主ループが設定の開き方で開く。
    private string? _launchRequest;

    /// <summary>トリガーで受け付けた操作（延長・リセット・目印・開く）の数。1回のトリガーを2か所で使わない判定と、振動の判定に使う。</summary>
    private int _actions;

    /// <summary>いま指している部品（振動の判定に使う→実装メモ5.78）。</summary>
    private PanelPointTarget PointedTarget()
    {
        if (_confirmOpen)
            return _confirmPointed >= 0 ? new PanelPointTarget(PanelPart.ConfirmItem, _confirmPointed) : PanelPointTarget.None;

        if (_popupOpen)
            return _pointedChoice >= 0 ? new PanelPointTarget(PanelPart.PopupChoice, _pointedChoice) : PanelPointTarget.None;

        if (_resetPointed)
            return new PanelPointTarget(PanelPart.Extend);

        if (_clearPointed)
            return new PanelPointTarget(PanelPart.Clear);

        return _hasHoverRow ? new PanelPointTarget(PanelPart.Row, _hoverRow.Index) : PanelPointTarget.None;
    }

    /// <summary>
    /// 見出しの「延長」と「リセット」を見て、指しているかと押されたかを決める。
    /// 「リセット」を押したら、確認を出す（→実装メモ5.65）。確認を出したなら true。
    ///
    /// 掴んでいる間は押せない。
    /// カーソルと同じく、一瞬外れただけでは指している扱いを外さない（手の微動で押し損ねないため）。
    /// </summary>
    private bool UpdateHeaderButtons(in InputFrame frame, TimeSpan now)
    {
        if (!PanelVisible || _grabbing || !frame.PointerPoseValid)
        {
            ClearResetPointing();
            return false;
        }

        if (TryPanelPoint(out var point))
        {
            var header = HeaderState();

            if (_renderer.ResetButtonRectFor(header).Contains(point))
                _resetHold.Signal(now);
            else if (_renderer.ClearButtonRectFor(header).Contains(point))
                _clearHold.Signal(now);
        }

        _resetPointed = _resetHold.IsHolding(now);
        _clearPointed = !_resetPointed && _clearHold.IsHolding(now);

        if (!frame.ClickPressed)
            return false;

        if (_resetPointed)
        {
            _resetPressed = true;
            _actions++;
            return false;
        }

        if (!_clearPointed)
            return false;

        ClearResetPointing();
        ClosePopup();
        ClearHoverRow();
        _confirmOpen = true;
        _confirmPointed = -1;
        return true;
    }

    private void ClearResetPointing()
    {
        _resetHold.Reset();
        _resetPointed = false;
        _clearHold.Reset();
        _clearPointed = false;
    }

    /// <summary>
    /// 履歴リセットの確認を出している間の更新（→実装メモ5.65）。
    /// 「リセット」を指して引けば消す依頼を立て、「キャンセル」か枠の外で引けば何もせずに閉じる。
    /// </summary>
    private void UpdateConfirm(in InputFrame frame)
    {
        if (_grabbing)
        {
            CloseConfirm();
            return;
        }

        var box = PanelGeometry.ConfirmBoxRect(_renderer.Style, _renderer.Height);
        _confirmPointed = frame.PointerPoseValid && TryPanelPoint(out var point) ? PanelGeometry.ConfirmItemAt(box, point) : -1;

        if (!frame.ClickPressed)
            return;

        if (_confirmPointed == PanelGeometry.ConfirmAccept)
        {
            _clearPressed = true;
            _actions++;
        }

        CloseConfirm();
    }

    private void CloseConfirm()
    {
        _confirmOpen = false;
        _confirmPointed = -1;
    }

    // ------------------------------------------------------------------ 目印（→実装メモ5.32）

    /// <summary>
    /// 指している行の帯と、目印を選ぶポップアップを進める（2026-09-22のユーザー指定）。
    ///
    /// 行を指すとその行がわずかに白くなり、そのままトリガーを引くと選択肢が出る。
    /// 選択肢を指してもう一度引くとその目印を付け、ポップアップの外で引くと何も変えずに閉じる。
    /// 既に付いている目印を選ぶと外れる（実際の付け外しは呼び出し側→<see cref="TakeMarkRequest"/>）。
    /// </summary>
    /// <param name="clickTaken">同じフレームのトリガーを見出しのボタンが既に使ったか。</param>
    private void UpdateMarking(in InputFrame frame, bool clickTaken)
    {
        // 掴んでいる間・行がない間は、どちらも出さない。
        // 「トリガーで操作メニューを表示」をオフにしている間も同じ（行を指しても白くしない→実装メモ5.86）。
        if (!PanelVisible || _grabbing || _layouts.Count == 0 || !_settings.TriggerMenuEnabled)
        {
            ClosePopup();
            ClearHoverRow();
            return;
        }

        if (_popupOpen)
        {
            UpdateOpenPopup(frame, clickTaken);
            return;
        }

        var hovering = TryFindHoverRow(out var hit);
        SetHoverRow(hovering, hit);

        if (hovering && !clickTaken && frame.ClickPressed)
            OpenPopup(hit);
    }

    /// <summary>ポップアップを開いている間の更新。指す行は開いたときの行に固定する。</summary>
    private void UpdateOpenPopup(in InputFrame frame, bool clickTaken)
    {
        var index = IndexOfRow(_popupEventId);

        // 開いたままの行が消えた（期限切れ・読み直し）ときは閉じる。
        if (index < 0)
        {
            ClosePopup();
            ClearHoverRow();
            return;
        }

        var hit = _renderer.RowAt(_layouts, index);
        _popupCurrent = _layouts[index].Row.Mark;
        _popupReturnEnabled = _layouts[index].Row.CanOpen(_returnAction);

        SetHoverRow(true, hit);
        _popupRect = PopupRectFor(hit);

        _pointedChoice = frame.PointerPoseValid ? PointedChoice() : -1;

        if (clickTaken || !frame.ClickPressed)
            return;

        if (_pointedChoice == PanelGeometry.ReturnChoiceIndex)
        {
            // 「ブラウザで開く」／「ここへ戻る」（→実装メモ5.43・5.53）。押せない行（「ここへ戻る」で滞在中）では PointedChoice が選ばない。
            _launchRequest = _popupEventId;
            _actions++;
        }
        else if (_pointedChoice >= 0)
        {
            _markRequest = (_popupEventId!, InstanceMarks.Choices[_pointedChoice]);
            _actions++;
        }

        // 選んでも、外を指して引いても閉じる。
        ClosePopup();
    }

    private void OpenPopup(PanelRenderer.RowHit hit)
    {
        _popupOpen = true;
        _popupEventId = _layouts[hit.Index].Row.EventId;
        _popupCurrent = _layouts[hit.Index].Row.Mark;
        _popupReturnEnabled = _layouts[hit.Index].Row.CanOpen(_returnAction);
        _pointedChoice = -1;
        _popupRect = PopupRectFor(hit);
    }

    private void ClosePopup()
    {
        if (!_popupOpen)
            return;

        _popupOpen = false;
        _popupEventId = null;
        _popupCurrent = InstanceMark.None;
        _popupReturnEnabled = false;
        _pointedChoice = -1;
        _popupRect = RectangleF.Empty;
    }

    /// <summary>指している行に重ねるポップアップの矩形（パネル内のpx座標）。</summary>
    private RectangleF PopupRectFor(PanelRenderer.RowHit hit)
    {
        var style = _renderer.Style;
        var row = PanelGeometry.RowHighlightRect(style, _renderer.ViewportHeight, _scroll.Offset, hit.Top, hit.Height, _scrollable);

        return PanelGeometry.MarkPopupRect(style, _renderer.ViewportHeight, row, InstanceMarks.Choices.Count);
    }

    /// <summary>
    /// レイが当たっている項目。目印の選択肢は 0 から、「ここへ戻る」は <see cref="PanelGeometry.ReturnChoiceIndex"/>。
    /// 当たっていない・押せない「ここへ戻る」を指しているときは -1。
    /// </summary>
    private int PointedChoice()
    {
        if (_popupRect.IsEmpty || !TryPanelPoint(out var point))
            return -1;

        var item = PanelGeometry.PopupItemAt(_popupRect, point, InstanceMarks.Choices.Count);
        return item == PanelGeometry.ReturnChoiceIndex && !_popupReturnEnabled ? -1 : item;
    }

    /// <summary>指している行を覚える。表示領域から出ている行は示さない。</summary>
    private void SetHoverRow(bool hovering, PanelRenderer.RowHit hit)
    {
        if (!hovering)
        {
            ClearHoverRow();
            return;
        }

        var rect = PanelGeometry.RowHighlightRect(
            _renderer.Style,
            _renderer.ViewportHeight,
            _scroll.Offset,
            hit.Top,
            hit.Height);

        if (rect.Height < 1f)
        {
            ClearHoverRow();
            return;
        }

        _hoverRow = hit;
        _hasHoverRow = true;
    }

    private void ClearHoverRow() => _hasHoverRow = false;

    /// <summary>右手のレイが当たっている行。当たっていない・行の間の帯を指しているときは false。</summary>
    private bool TryFindHoverRow(out PanelRenderer.RowHit hit)
    {
        hit = default;

        if (_layouts.Count == 0 || !TryPanelPoint(out var point))
            return false;

        var style = _renderer.Style;

        if (point.X < 0f || point.X > PanelGeometry.RowHighlightRight(style, _scrollable))
            return false;

        var viewportTop = style.ViewportTop;

        if (point.Y < viewportTop || point.Y >= viewportTop + _renderer.ViewportHeight)
            return false;

        return _renderer.TryHitRow(_layouts, _scroll.Offset + (point.Y - viewportTop), out hit);
    }

    private int IndexOfRow(string? eventId)
    {
        if (eventId is null)
            return -1;

        for (var i = 0; i < _layouts.Count; i++)
        {
            if (string.Equals(_layouts[i].Row.EventId, eventId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
