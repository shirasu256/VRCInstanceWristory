namespace VRCInstanceWristory.Vr;

/// <summary>
/// 閉じるボタン（左手の Y・右手の B）の短押しを、手ごとに別々に拾う検出器（→実装メモ5.96）。
///
/// VRChatのメインメニューは、片方を押し続けている間にもう片方を短押ししても閉じる。
/// 左右をまとめて「どちらかが押されている」として1つの <see cref="TapDetector"/> で見ると、
/// 押し続けている側のせいで押下が途切れず、もう片方の短押しを取りこぼす。
/// そのため手ごとに短押しを判定し、どちらかで成立すれば閉じる合図とする。
/// </summary>
public sealed class TwoHandTapDetector(TimeSpan maxHold)
{
    private readonly TapDetector _left = new(maxHold);
    private readonly TapDetector _right = new(maxHold);

    /// <summary>この時間以内に離せば短押しとみなす。</summary>
    public TimeSpan MaxHold { get; } = maxHold;

    /// <summary>
    /// 左右それぞれが押されているかを毎フレーム渡す。どちらかの手で短押しが成立したフレームだけ true。
    /// 同じフレームで両手が成立しても1回として返す。
    /// </summary>
    public bool Update(bool leftHeld, bool rightHeld, TimeSpan now)
    {
        // 両方を必ず更新する（片方で成立しても、もう片方の押下の追跡を進める）。
        var left = _left.Update(leftHeld, now);
        var right = _right.Update(rightHeld, now);
        return left || right;
    }

    /// <summary>押下の追跡を捨てる。</summary>
    public void Reset()
    {
        _left.Reset();
        _right.Reset();
    }
}
