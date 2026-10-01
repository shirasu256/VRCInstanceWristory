using System.Globalization;
using VRCInstanceWristory.Core.History;

namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 見出し右に出す「履歴を消すまでの残り時間」（2026-09-18のユーザー指定）。
///
/// 対象インスタンスに滞在している間は期限を数え始めないので、上限（保持時間・既定60分）で止めて `60:00` を出す。
/// 表示は切り上げで、残り0になった瞬間に `00:00` になる。
/// 保持時間は設定で最大 <see cref="HistoryStore.MaxRetentionMinutes"/>（900分）まで延ばせる（→実装メモ5.39）。`99:59` を超える残り時間は、分を3桁にせず
/// `1:40:00` のように時・分・秒で出す（2026-09-27のユーザー指定→実装メモ5.67）。
/// 数え始めが `1:40:00` 以上なら、減って100分を切っても形を変えない（`99:59` ではなく `1:39:59`、`59:00` ではなく `0:59:00`。
/// 2026-09-28のユーザー指定→実装メモ5.86）。途中で桁の形が変わると、読み違えやすく、見出しの幅も揺れるため。
/// </summary>
public static class Countdown
{
    /// <summary>既定の上限。履歴の既定の保持時間と同じ。実際の上限は <see cref="EngineSnapshot.Retention"/>。</summary>
    public static readonly TimeSpan Max = HistoryStore.DefaultRetention;

    /// <summary>表示できる上限（設定で選べる保持時間の最大）。</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(HistoryStore.MaxRetentionMinutes);

    /// <summary>残り時間を分と秒へ分ける。範囲外は 0〜<see cref="Limit"/> へ丸める。</summary>
    public static (int Minutes, int Seconds) Parts(TimeSpan remaining)
    {
        var total = (int)Math.Ceiling(remaining.TotalSeconds);
        total = Math.Clamp(total, 0, (int)Limit.TotalSeconds);

        return (total / 60, total % 60);
    }

    /// <summary>100分。これ以上から数えるときは `H:MM:SS` で出す。</summary>
    public static readonly TimeSpan HoursFrom = TimeSpan.FromMinutes(100);

    /// <summary>
    /// `MM:SS` の文字列。`99:59` を超えるときは `H:MM:SS`（`1:40:00`・`3:00:00`）。
    /// <paramref name="total"/>（数え始めの長さ＝保持時間）が100分以上なら、残りが100分を切っても `H:MM:SS`（`1:39:59`・`0:59:59`）。
    /// </summary>
    public static string Format(TimeSpan remaining, TimeSpan? total = null)
    {
        var (minutes, seconds) = Parts(remaining);

        return minutes < 100 && !(total >= HoursFrom)
            ? string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", minutes, seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", minutes / 60, minutes % 60, seconds);
    }
}
