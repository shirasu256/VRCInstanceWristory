namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// いつ予告のアイコンを出すかを決める（→実装メモ5.87・5.89）。
///
/// <see cref="ResetWarningTrigger"/> が知らせる瞬間を決め、ここでは「見逃したかもしれない」場面を拾い直す。
/// 利用者がそこにいない間（VRChat の AFK・SteamVR のダッシュボードを開いている）に知らせる瞬間が来た、
/// または点滅の途中でいなくなったときは、戻ってきた時点でまだリセットされていなければもう一度出す（「AFKから復帰時に再表示する」）。
/// </summary>
public sealed class ResetWarningScheduler
{
    private readonly ResetWarningTrigger _trigger = new();
    private bool _missed;
    private bool _wasAway;
    private DateTime? _shownUntilUtc;

    /// <summary>1回分の判断。出す瞬間なら true（呼び出し側が点滅を始める）。</summary>
    /// <param name="countingDown">自動リセットを使っていて期限があるか（AFK でカウントダウンを止めている間も含む）。</param>
    /// <param name="away">利用者がいまそこにいないか（AFK・ダッシュボード）。</param>
    /// <param name="reshow">「AFKから復帰時に再表示する」。false なら、いない間も出す（見逃しても出し直さない）。</param>
    /// <param name="blinkDuration">点滅1回ぶんの長さ（点滅の途中でいなくなったかを見る）。</param>
    public bool Observe(
        DateTime nowUtc,
        bool countingDown,
        TimeSpan remaining,
        bool relevant,
        TimeSpan lead,
        bool away,
        bool reshow,
        TimeSpan blinkDuration)
    {
        var fire = _trigger.Observe(countingDown, remaining, relevant, lead);
        var returned = _wasAway && !away;
        var leaving = !_wasAway && away;
        _wasAway = away;

        if (!reshow)
        {
            _missed = false;

            if (fire)
                _shownUntilUtc = nowUtc + blinkDuration;

            return fire;
        }

        // 点滅の途中でいなくなった（HMD を外した・ダッシュボードを開いた）。
        if (leaving && _shownUntilUtc is { } until && nowUtc < until)
            _missed = true;

        if (fire)
        {
            if (away)
            {
                _missed = true;
                return false;
            }

            _shownUntilUtc = nowUtc + blinkDuration;
            return true;
        }

        if (!_missed)
            return false;

        // まだリセットされていない（数えていて残りがあり、表示タイミングの内側）ときだけ出し直す。
        // 延長で表示タイミングより上へ戻った・対象へ戻って停止した・消えたなら、もう知らせない。
        // AFK の間カウントダウンを止めている（→実装メモ5.89）ときも、期限はあるので「数えている」に入れて渡す。
        if (!countingDown || !relevant || remaining <= TimeSpan.Zero || remaining > lead)
        {
            _missed = false;
            return false;
        }

        if (!returned)
            return false;

        _missed = false;
        _shownUntilUtc = nowUtc + blinkDuration;
        return true;
    }
}
