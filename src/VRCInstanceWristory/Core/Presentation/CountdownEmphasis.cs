namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 見出しの残り時間（数字）の強調（2026-10-05のユーザー指定→実装メモ5.130）。
///
/// | 強調 | いつ | 見た目 |
/// | --- | --- | --- |
/// | 発光 | 「延長」を押して数え直したとき | 押した瞬間に数字が光り、<see cref="GlowDuration"/> かけて通常の表示へ戻る |
/// | 警告 | 自動リセットまで残り <see cref="WarningFrom"/> 以下の間 | <see cref="WarningPeriod"/> 周期で、通常の色と赤みがかった色の間を滑らかに行き来する |
///
/// どちらも 0〜1 の強さで返し、色や光の描き方は描く側（<c>PanelRenderer</c>）が決める。
/// 強さは丸めずに、渡された時間そのものから求める。描き直す回数は表示側が <see cref="CountdownEmphasisPacer"/> で
/// 約 <see cref="FrameRate"/>（45回/秒）までに抑える（→実装メモ5.133）。
///
/// | 版 | 描き直しの決め方 | 困ったこと |
/// | --- | --- | --- |
/// | 5.130 | 強さの値を24段に丸め、段が変わったら | 値の変わる速さが一定でないので、色の折り返しの前後や光の消え際で数回/秒まで落ちた |
/// | 5.132 | 時間を時計の上の1/45秒の刻みに丸め、刻みをまたいだら | 主ループの回り方が45の倍数からずれると（VRChat の負荷で上下する）、決まった間隔で刻みが飛ぶ・止まる（50回/秒なら0.2秒ごと） |
/// | 5.133 | 前に描いてから約1/45秒たったフレームで、そのときの時刻から | — |
/// </summary>
public static class CountdownEmphasis
{
    /// <summary>発光が消えるまでの時間（2026-10-05のユーザー指定。初めの1.5秒から同日に1秒へ縮めた）。</summary>
    public static readonly TimeSpan GlowDuration = TimeSpan.FromSeconds(1);

    /// <summary>警告の色を出し始める残り時間（2026-10-05のユーザー指定）。</summary>
    public static readonly TimeSpan WarningFrom = TimeSpan.FromMinutes(3);

    /// <summary>警告の色が1往復する周期（2026-10-05のユーザー指定。初めの2秒から同日に5秒、さらに3.5秒へ改めた）。</summary>
    public static readonly TimeSpan WarningPeriod = TimeSpan.FromSeconds(3.5);

    /// <summary>強調が動いている間に描き直す回数の目安（回/秒→<see cref="CountdownEmphasisPacer"/>・2026-10-06のユーザー指定）。</summary>
    public const int FrameRate = 45;

    /// <summary>
    /// 「延長」を押してから <paramref name="sinceExtended"/> 経ったときの発光の強さ（1→0）。押していなければ（null）0。
    /// 初めに強く光ってから素早く引き、最後はゆっくり消える（2乗で減らす）。
    /// </summary>
    public static float Glow(TimeSpan? sinceExtended)
    {
        if (sinceExtended is not { } since || since < TimeSpan.Zero || since >= GlowDuration)
            return 0f;

        var rest = 1f - (float)(since / GlowDuration);
        return rest * rest;
    }

    /// <summary>
    /// 残り時間 <paramref name="remaining"/> のときの警告の強さ（0＝通常の色・1＝赤みがかった色）。
    /// 残りが <see cref="WarningFrom"/> を超えている・数えていない（止まっている・自動リセットを使っていない）なら 0。
    ///
    /// 位相は時計ではなく残り時間から決める。残りが3分を切った瞬間が通常の色（0）なので、切り替わりで色が跳ばない。
    /// 手首のパネルとデスクトップのウィンドウも、同じ残り時間から同じ色になる。
    /// </summary>
    public static float Warning(TimeSpan remaining, bool countingDown)
    {
        if (!countingDown || remaining > WarningFrom || remaining < TimeSpan.Zero)
            return 0f;

        var phase = (WarningFrom - remaining) / WarningPeriod;
        return Math.Clamp((1f - MathF.Cos((float)(phase * 2.0 * Math.PI))) / 2f, 0f, 1f);
    }

    /// <summary>
    /// 強調が時間とともに動いているか。動いている間は、表示側が数字の変わり目（1秒ごと）を待たずに描き直す。
    /// 警告は残り時間から決まるので、<paramref name="remaining"/> が3分以下なら動いている。
    /// </summary>
    public static bool Animating(TimeSpan? sinceExtended, TimeSpan remaining, bool countingDown)
        => (sinceExtended is { } since && since >= TimeSpan.Zero && since < GlowDuration)
           || (countingDown && remaining >= TimeSpan.Zero && remaining <= WarningFrom);
}
