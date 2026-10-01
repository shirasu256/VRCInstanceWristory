namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 状態の段の点の点滅（2026-09-29のユーザー指定→実装メモ5.91）。赤は1秒周期に2値で、黄は5秒周期で穏やかに点滅する。
/// ほかの色は点滅しない。時刻を渡すと、その時点の点の不透明度（0〜1・点の色の不透明度に掛ける）を返す。
/// </summary>
public static class StatusBlink
{
    /// <summary>赤の周期。前半は点け、後半は薄くする。</summary>
    private static readonly TimeSpan ErrorPeriod = TimeSpan.FromSeconds(1);

    /// <summary>赤の消えている側の不透明度。0 にすると点が消えて行の頭がずれて見えるので、うっすら残す。</summary>
    public const float ErrorLow = 0.15f;

    /// <summary>黄の周期。</summary>
    private static readonly TimeSpan WarningPeriod = TimeSpan.FromSeconds(5);

    /// <summary>黄のいちばん薄いときの不透明度。</summary>
    public const float WarningLow = 0.3f;

    /// <summary>その色の点は点滅するか。</summary>
    public static bool Blinks(StatusTone tone) => tone is StatusTone.Error or StatusTone.Warning;

    /// <summary>その時点の点の不透明度。時刻 0 ではどの色も 1（見本の画像は点いた状態で描く）。</summary>
    public static float Alpha(StatusTone tone, TimeSpan time)
    {
        switch (tone)
        {
            case StatusTone.Error:
            {
                var t = time.TotalSeconds % ErrorPeriod.TotalSeconds;
                return t < ErrorPeriod.TotalSeconds / 2 ? 1f : ErrorLow;
            }

            case StatusTone.Warning:
            {
                // 余弦で滑らかに薄くして戻す（1 → 0.3 → 1）。
                var phase = time.TotalSeconds / WarningPeriod.TotalSeconds * 2 * Math.PI;
                var wave = (float)((1 + Math.Cos(phase)) / 2);
                return WarningLow + ((1f - WarningLow) * wave);
            }

            default:
                return 1f;
        }
    }
}
