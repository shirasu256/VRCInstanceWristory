using System.Globalization;

namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 予告のアイコンの設定で選べる値（2026-09-29のユーザー指定→実装メモ5.89）。どれも小さい順。
/// </summary>
public static class ResetWarningOptions
{
    /// <summary>表示サイズ（マイクと同じ大きさ＝1を真ん中にした5段階）。</summary>
    public static readonly float[] Scales = [0.6f, 0.8f, 1f, 1.2f, 1.4f];

    /// <summary>不透明度。50〜90%は5%、90〜100%は2%刻み。</summary>
    public static readonly float[] Opacities =
    [
        .. Enumerable.Range(10, 8).Select(i => (float)Math.Round(i * 0.05, 2)),        // 0.50〜0.85
        .. Enumerable.Range(0, 6).Select(i => (float)Math.Round(0.90 + (i * 0.02), 2)), // 0.90〜1.00
    ];

    /// <summary>点滅回数。</summary>
    public static readonly int[] BlinkCounts = [1, 3, 5, 10, 30];

    /// <summary>表示タイミング（リセットの何分前に出すか）。</summary>
    public static readonly int[] LeadMinutes = [1, 3, 5];

    public const float DefaultScale = 1f;

    public const float DefaultOpacity = 1f;

    public const int DefaultBlinkCount = 5;

    public const int DefaultLeadMinutes = 3;

    /// <summary>
    /// マイクアイコンの位置の合わせ（横・縦とも cm・右と上が正→実装メモ5.92）。VRChat は VR のマイクのアイコンの位置を外へ出しておらず、
    /// 借りてきた位置（VRCMicOverlay の既定）が利用者のヘッドセットで合うとは限らないので、実物に合わせて動かせるようにした。
    /// </summary>
    public const float MicOffsetLimitCm = 20f;

    public const float MicOffsetStepCm = 0.5f;

    /// <summary>
    /// 奥行きの合わせ（cm・奥が正→実装メモ5.95）。見える方向と見かけの大きさは保ったまま、距離だけを変える。
    /// 横・縦より大きく動かさないと違いが分からないので、刻みを粗くした。
    /// </summary>
    public const float MicDepthLimitCm = 50f;

    public const float MicDepthStepCm = 5f;

    /// <summary>奥行きの合わせの書式（<c>奥 10 cm</c>・<c>手前 5 cm</c>・<c>0 cm</c>）。</summary>
    public static string FormatMicDepth(float cm)
    {
        if (MathF.Abs(cm) < 0.01f)
            return "0 cm";

        return string.Format(CultureInfo.InvariantCulture, "{0} {1:0} cm", cm > 0 ? "奥" : "手前", MathF.Abs(cm));
    }

    /// <summary>マイクアイコンの位置の合わせの書式（<c>右 1.5 cm</c>・<c>0.0 cm</c>）。</summary>
    public static string FormatMicOffset(float cm, bool vertical)
    {
        if (MathF.Abs(cm) < 0.01f)
            return "0.0 cm";

        var side = vertical ? (cm > 0 ? "上" : "下") : (cm > 0 ? "右" : "左");
        return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0} cm", side, MathF.Abs(cm));
    }

    /// <summary>並びの中で次の値（端では止まる）。並びにない値からは、その方向でいちばん近い値へ動かす。</summary>
    public static float Next(float[] steps, float value, int direction)
        => direction > 0
            ? steps.FirstOrDefault(v => v > value + 0.001f, steps[^1])
            : steps.LastOrDefault(v => v < value - 0.001f, steps[0]);

    public static string FormatPercent(float value) => string.Format(CultureInfo.InvariantCulture, "{0:0} %", value * 100f);
}
