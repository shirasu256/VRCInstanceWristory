namespace VRCInstanceWristory.Vr;

/// <summary>
/// 「押して、しきい値より短い時間で離した」＝短押しだけを拾う検出器。長押しでは何も返さない。
///
/// VRChatのメインメニューは B / Y の短押しで開閉し、長押しでは閉じない。
/// 押した瞬間で拾うと、長押ししてメニューが開いたままなのにパネルだけ消えてしまうため、
/// 離すまで待って押していた長さで判断する（5.25）。
/// </summary>
public sealed class TapDetector(TimeSpan maxHold)
{
    // 押していない、または長押しが確定した後は null。
    private TimeSpan? _pressedAt;
    private bool _held;

    /// <summary>この時間以内に離せば短押しとみなす。</summary>
    public TimeSpan MaxHold { get; } = maxHold;

    /// <summary>押されているかを毎フレーム渡す。短押しが成立したフレームだけ true。</summary>
    public bool Update(bool held, TimeSpan now)
    {
        var tapped = false;

        if (held && !_held)
        {
            // 押し始め。まだ短押しか長押しかは決まらない。
            _pressedAt = now;
        }
        else if (!held && _held)
        {
            // 離した。しきい値の内側なら短押し。長押し確定後は _pressedAt が null なので成立しない。
            tapped = _pressedAt is { } start && now - start <= MaxHold;
            _pressedAt = null;
        }
        else if (held && _pressedAt is { } since && now - since > MaxHold)
        {
            // 押しっぱなしでしきい値を越えた。この押下はもう短押しにならない。
            _pressedAt = null;
        }

        _held = held;
        return tapped;
    }

    /// <summary>押下の追跡を捨てる。</summary>
    public void Reset()
    {
        _pressedAt = null;
        _held = false;
    }
}
