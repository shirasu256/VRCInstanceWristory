namespace VRCInstanceWristory.Vr;

/// <summary>
/// 「条件が最後に成立してから一定時間は成立しているとみなす」小さなタイマー。
///
/// 追跡の一瞬の欠落や、レイの命中の取りこぼしでオーバーレイが点滅しないようにするために使う。
/// インサイドアウト追跡では、右手で左手を隠すだけで数フレーム追跡が切れることがある。
/// </summary>
public sealed class GraceTimer(TimeSpan grace)
{
    // 未成立は null で表す。番兵（TimeSpan.MinValue）を引き算すると桁あふれで落ちる。
    private TimeSpan? _lastOk;

    public TimeSpan Grace { get; } = grace;

    /// <summary>条件が成立した。</summary>
    public void Signal(TimeSpan now) => _lastOk = now;

    /// <summary>条件が成立している、または猶予の内側か。</summary>
    public bool IsHolding(TimeSpan now) => _lastOk is { } last && now - last <= Grace;

    /// <summary>猶予を打ち切る（対象が変わったときなど）。</summary>
    public void Reset() => _lastOk = null;
}
