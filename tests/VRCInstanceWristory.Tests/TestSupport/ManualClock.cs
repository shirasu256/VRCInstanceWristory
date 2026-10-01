using VRCInstanceWristory.Core;

namespace VRCInstanceWristory.Tests;

/// <summary>検証用の時計。時刻を明示的に進める。</summary>
public sealed class ManualClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
