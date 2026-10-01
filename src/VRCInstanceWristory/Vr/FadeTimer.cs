namespace VRCInstanceWristory.Vr;

/// <summary>
/// 表示・非表示を一定時間かけて渡す不透明度。
///
/// 視線角度の条件は「規定内か規定外か」の二値のまま扱い、切り替わった時点から
/// 決まった秒数で 0 ↔ 1 を往復させる。角度そのものを不透明度へ写す案は実機で試したが、
/// 首の動きに追従して濃さが揺れるのが落ち着かなかったため、時間で渡す方式に戻した
/// （2026-09-19のユーザー指定）。
/// </summary>
public sealed class FadeTimer(TimeSpan duration)
{
    private float _seconds = duration > TimeSpan.Zero ? (float)duration.TotalSeconds : 0f;

    /// <summary>
    /// 渡しきるまでの時間。0 なら一瞬で切り替わる。
    /// デスクトップのウィンドウから実行中に変えられる（→実装メモ5.39）。変えても今の不透明度は保ち、
    /// 残りを新しい速さで渡す。
    /// </summary>
    public TimeSpan Duration
    {
        get;
        set
        {
            field = value > TimeSpan.Zero ? value : TimeSpan.Zero;
            _seconds = (float)field.TotalSeconds;
        }
    } = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;

    /// <summary>現在の不透明度。1 が完全に不透明、0 で見えていない。</summary>
    public float Alpha { get; private set; } = 1f;

    /// <summary>まだ少しでも見えているか。0 になったら隠してよい。</summary>
    public bool Visible => Alpha > 0f;

    /// <param name="target">見せたいか。true なら 1 へ、false なら 0 へ向かう。</param>
    /// <param name="deltaSeconds">前フレームからの経過。</param>
    public float Update(bool target, float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
            deltaSeconds = 0f;

        var step = _seconds > 0f ? deltaSeconds / _seconds : 1f;

        Alpha = target
            ? MathF.Min(1f, Alpha + step)
            : MathF.Max(0f, Alpha - step);

        return Alpha;
    }

    /// <summary>途中を飛ばして合わせる（別の理由で隠れている間など）。</summary>
    public void Snap(bool target) => Alpha = target ? 1f : 0f;
}
