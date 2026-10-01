namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 知らせるアイコンの点滅（2秒間隔で <see cref="Count"/> 回→実装メモ5.87・5.89）。時刻を渡すと、その時点の不透明度（0〜1）を返す。
///
/// 1周期（2秒）のうち、<see cref="FadeSeconds"/> で現れ、<see cref="OnSeconds"/> まで出し続け、<see cref="FadeSeconds"/> で消える。
/// オンとオフを切り替えるだけだとHMDの中では瞬きのように強く見えるので、両端だけ短く渡す（→実装メモ5.16）。
/// </summary>
public sealed class ResetWarningBlink
{
    /// <summary>点滅の間隔。</summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(2);

    /// <summary>既定の点滅の回数。</summary>
    public const int DefaultCount = ResetWarningOptions.DefaultBlinkCount;

    /// <summary>1周期のうち出している時間（現れる・消えるを含む）。</summary>
    public const float OnSeconds = 1.2f;

    /// <summary>現れる・消えるのにかける時間。</summary>
    public const float FadeSeconds = 0.15f;

    /// <summary>点滅 <paramref name="count"/> 回の全体の長さ（最後の周期の消えている時間まで）。</summary>
    public static TimeSpan DurationFor(int count) => Period * Math.Max(1, count);

    private TimeSpan? _start;

    /// <summary>いま始めている点滅の回数。</summary>
    public int Count { get; private set; } = DefaultCount;

    /// <summary>いま始めている点滅の全体の長さ。</summary>
    public TimeSpan Duration => DurationFor(Count);

    /// <summary>点滅している最中か（<see cref="Duration"/> を過ぎるまで）。</summary>
    public bool Active(TimeSpan now) => _start is { } start && now >= start && now - start < Duration;

    /// <summary>点滅を最初から始める。点滅中に呼ぶと数え直す。</summary>
    public void Start(TimeSpan now, int count = DefaultCount)
    {
        _start = now;
        Count = Math.Max(1, count);
    }

    /// <summary>やめる。</summary>
    public void Stop() => _start = null;

    /// <summary>その時点の不透明度（0〜1）。点滅していなければ 0。</summary>
    public float Alpha(TimeSpan now)
    {
        if (!Active(now))
            return 0f;

        var t = (float)((now - _start!.Value).TotalSeconds % Period.TotalSeconds);

        if (t >= OnSeconds)
            return 0f;

        var alpha = MathF.Min(t / FadeSeconds, (OnSeconds - t) / FadeSeconds);
        return Math.Clamp(alpha, 0f, 1f);
    }
}
