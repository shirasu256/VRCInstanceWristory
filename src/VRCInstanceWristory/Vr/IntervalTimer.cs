namespace VRCInstanceWristory.Vr;

/// <summary>
/// 「前回から一定時間が経ったか」を判定する小さなタイマー。
///
/// 最後の時刻は未設定を null で表す。<c>TimeSpan.MinValue</c> のような番兵を置いて
/// <c>now - 番兵</c> を計算すると桁あふれ（OverflowException）で落ちるため、
/// 経過時間の比較はこの型に集約する。
/// </summary>
public sealed class IntervalTimer(TimeSpan interval)
{
    private TimeSpan? _last;

    public TimeSpan Interval { get; } = interval;

    /// <summary>一度も動いていない、または間隔を過ぎているか。</summary>
    public bool IsDue(TimeSpan now) => _last is not { } last || now - last >= Interval;

    /// <summary>間隔を過ぎていれば時刻を更新して true を返す。</summary>
    public bool TryTick(TimeSpan now)
    {
        if (!IsDue(now))
            return false;

        _last = now;
        return true;
    }

    public void Reset() => _last = null;
}
