using System.Drawing;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 行の下絵の上に重ねる、フレームごとに変わるもの。
/// 行と同じ1枚へ描く（→実装メモ5.35）。見出しの右側（<see cref="Countdown"/>・<see cref="AutoResetDisabled"/>・
/// <see cref="CountdownStopped"/>）は、描くときと命中を見るときで同じ値を使う（<see cref="PanelRenderer.ResetButtonRectFor(in PanelDecorations)"/>）。
/// </summary>
public readonly record struct PanelDecorations
{
    /// <summary>見出し右の残り時間（<c>MM:SS</c>）。null なら残り時間もボタンも描かない。</summary>
    public string? Countdown { get; init; }

    /// <summary>「延長」のボタンを指しているか。</summary>
    public bool ResetPointed { get; init; }

    /// <summary>
    /// 履歴の自動リセットを無効にしているか（→実装メモ5.71）。見出しの残り時間・「延長」の代わりに
    /// 「履歴の自動リセットが無効化されています」を出し、見出しの左の時刻に日付を付ける。
    /// </summary>
    public bool AutoResetDisabled { get; init; }

    /// <summary>
    /// カウントダウンが止まっているか（対象インスタンスに滞在中など→実装メモ5.73）。
    /// 「履歴リセットまで:」を「カウントダウン停止中:」にし、「延長」「リセット」を薄くして押せなくする。
    /// </summary>
    public bool CountdownStopped { get; init; }

    /// <summary>「リセット」のボタンを指しているか（→実装メモ5.65）。</summary>
    public bool ClearPointed { get; init; }

    /// <summary>履歴リセットの確認を出しているか（→実装メモ5.65）。出している間は、ほかの飾りの上に重ねる。</summary>
    public bool ConfirmClear { get; init; }

    /// <summary>
    /// 確認の中で指しているボタン（<see cref="PanelGeometry.ConfirmCancel"/>・<see cref="PanelGeometry.ConfirmAccept"/>）。
    /// どれも指していなければ -1。
    /// </summary>
    public int ConfirmPointed { get; init; }

    /// <summary>指している行の帯（パネル内のpx座標）。高さ0なら描かない。</summary>
    public RectangleF HoverRow { get; init; }

    /// <summary>目印のポップアップ（パネル内のpx座標）。空なら描かない。</summary>
    public RectangleF Popup { get; init; }

    /// <summary>その行にいま付いている目印。</summary>
    public Core.Marks.InstanceMark PopupCurrent { get; init; }

    /// <summary>ポップアップの中で指している選択肢。</summary>
    public Core.Marks.InstanceMark PopupPointed { get; init; }

    /// <summary>ポップアップの右端のボタンで開くもの（→実装メモ5.53）。文字と印が「ブラウザで開く」／「ここへ戻る」に変わる。</summary>
    public Core.Locations.ReturnAction PopupReturnAction { get; init; }

    /// <summary>ポップアップの右端のボタンを押せるか（→実装メモ5.43）。「ここへ戻る」はいま滞在しているインスタンスでは押せない。</summary>
    public bool PopupReturnEnabled { get; init; }

    /// <summary>ポップアップの「ここへ戻る」を指しているか。</summary>
    public bool PopupReturnPointed { get; init; }

    /// <summary>
    /// デスクトップのウィンドウで選んでいる行（パネル内のpx座標・→実装メモ5.42）。高さ0なら描かない。
    /// VR内では使わない（右手のレイで行を「選ぶ」操作はない）。
    /// </summary>
    public RectangleF SelectedRow { get; init; }
}
