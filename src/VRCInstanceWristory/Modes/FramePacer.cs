namespace VRCInstanceWristory.Modes;

/// <summary>
/// 主ループの間隔をそろえる。処理時間を差し引いて待つので、描画が重いフレームでも
/// 目標周期より遅れ続けない（姿勢の反映が遅れるとカーソルやパネルがちらついて見える）。
/// </summary>
public sealed class FramePacer(TimeSpan interval)
{
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _next = TimeSpan.Zero;

    public void Wait()
    {
        var now = _clock.Elapsed;

        if (_next < now)
            _next = now;

        var remaining = _next - now;
        _next += interval;

        if (remaining > TimeSpan.Zero)
            Thread.Sleep(remaining);
    }
}
