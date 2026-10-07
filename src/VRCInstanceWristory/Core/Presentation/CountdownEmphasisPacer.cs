namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 残り時間の数字の強調（→<see cref="CountdownEmphasis"/>）を描き直す間隔を、約 <see cref="CountdownEmphasis.FrameRate"/>（45回/秒）までに抑える
/// （2026-10-06のユーザー指定→実装メモ5.133）。手首のパネルは主ループの各フレームでこれを通す。
///
/// 強さは丸めずにそのフレームの時刻から求めたものを受け取り、前に出してから <see cref="Interval"/> たっていれば出し直す。
/// 時計の上の刻みに合わせない（→5.132 の失敗）ので、主ループの回り方が VRChat の負荷で上下しても、刻みが飛ぶことも決まった間隔で止まることもない。
///
/// | 主ループ | 描き直し |
/// | --- | --- |
/// | 45回/秒より速い（90回/秒など） | 期限（前の期限 + <see cref="Interval"/>）を過ぎたフレームごと。平均で約45回/秒 |
/// | 45回/秒以下 | 毎フレーム |
/// </summary>
public sealed class CountdownEmphasisPacer
{
    /// <summary>描き直しの間隔（1/45秒）。</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1.0 / CountdownEmphasis.FrameRate);

    /// <summary>
    /// 期限の手前でも出し直してよい幅。90回/秒の主ループでは2フレームに1回がちょうど期限に当たるので、
    /// 待ちの揺れ（1ms前後）で期限の直前に来たフレームを見送って3フレームに1回へ落ちないようにする。
    /// </summary>
    public static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(3);

    private TimeSpan _next;
    private (float Glow, float Warning) _shown;

    /// <summary>
    /// このフレームに出す強さ。<paramref name="glow"/>・<paramref name="warning"/> はこのフレームの時刻から求めた値。
    /// 間隔がたっていなければ前に出した値を返す（描く側は見た目が同じなら描き直さない）。
    /// 動き始め（前が0）と止まったとき（両方0）は、間隔を待たずにすぐ出す。
    /// </summary>
    public (float Glow, float Warning) Next(TimeSpan now, float glow, float warning)
    {
        var value = (glow, warning);

        if (value == _shown)
            return _shown;

        var starting = _shown == (0f, 0f);
        var stopping = value == (0f, 0f);

        if (!starting && !stopping && now + Slack < _next)
            return _shown;

        // 期限から1間隔以上遅れていたら（主ループが45回/秒より遅い・しばらく変わらなかった）、いまから数え直す。
        // 遅れを取り返そうとして続けて出すことはしない。
        _next = starting || now - _next >= Interval ? now + Interval : _next + Interval;
        _shown = value;
        return value;
    }
}
