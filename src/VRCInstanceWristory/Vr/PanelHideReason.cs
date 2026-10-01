namespace VRCInstanceWristory.Vr;

/// <summary>パネルを隠した理由。ちらつきの原因を切り分けるために数える。</summary>
public enum PanelHideReason
{
    None,

    /// <summary>ログ側の条件（実行中・ワールドタブ・期限内の履歴）が不成立。</summary>
    ContentNotReady,

    /// <summary>パネルを付けた手（既定は左手）の追跡が失われた（猶予を過ぎた）。</summary>
    WristTrackingLost,

    /// <summary>
    /// パネルを付けた手のコントローラーが見つからない（電源が入っていない、など→実装メモ5.85）。
    /// それまでは <see cref="WristTrackingLost"/> と同じに扱っていた。
    /// </summary>
    ControllerMissing,

    /// <summary>視線とパネル正面の角度が規定外。</summary>
    ViewAngle,

    /// <summary>絵をまだ一度も渡せていない。</summary>
    TextureNotReady,
}
