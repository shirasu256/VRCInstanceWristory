namespace VRCInstanceWristory.Vr;

/// <summary>
/// 同じ失敗を続けてログへ出すときの間引き。種類（操作の名前など）ごとに、最初の1回はすぐに出し、
/// あとは <see cref="Interval"/> に1回だけ、その間に出さずにおいた回数を添えて出す。
///
/// SteamVR への呼び出しは失敗しても毎フレーム（90Hz）試し直すので、そのたびに出すとログが埋まる。
/// </summary>
public sealed class LogThrottle(TimeSpan interval)
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public TimeSpan Interval { get; } = interval;

    /// <summary>
    /// <paramref name="kind"/> の失敗が起きた。ログへ出すなら true で、<paramref name="suppressed"/> は前に出してから出さずにおいた回数。
    /// </summary>
    public bool ShouldLog(string kind, TimeSpan now, out int suppressed)
    {
        if (!_entries.TryGetValue(kind, out var entry))
            _entries[kind] = entry = new Entry(new IntervalTimer(Interval));

        if (!entry.Timer.TryTick(now))
        {
            entry.Suppressed++;
            suppressed = 0;
            return false;
        }

        suppressed = entry.Suppressed;
        entry.Suppressed = 0;
        return true;
    }

    private sealed class Entry(IntervalTimer timer)
    {
        public IntervalTimer Timer { get; } = timer;

        public int Suppressed { get; set; }
    }
}
