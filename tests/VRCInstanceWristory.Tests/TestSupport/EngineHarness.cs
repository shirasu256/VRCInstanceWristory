using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Tests;

/// <summary>検証用のエンジン一式。</summary>
public sealed class EngineHarness : IDisposable
{
    public static readonly TimeZoneInfo Tokyo = ResolveTokyo();

    public EngineHarness(
        string logDirectory,
        DateTime nowLocal,
        ClientProcessInfo? process,
        CheckpointStore? checkpoint = null,
        Func<EngineOptions, EngineOptions>? configure = null,
        Core.Marks.MarkFile? marks = null,
        bool followLogTime = false,
        Core.History.HistoryFile? history = null)
    {
        Time = new LogTimeConverter(Tokyo);
        Time.TryToUtc(nowLocal, out var nowUtc);
        Clock = new ManualClock(nowUtc);
        Processes = new FixedProcessProvider(process);
        Diagnostics = new CollectingDiagnostics();

        var options = new EngineOptions
        {
            LogDirectory = logDirectory,
            PersistCheckpoint = checkpoint is not null,

            // 提供ログの再生では、処理した行の時刻へ時計を進める。
            OnLogTime = followLogTime ? AdvanceTo : null,
        };

        if (configure is not null)
            options = configure(options);

        Engine = new HistoryEngine(options, Clock, Time, Processes, Diagnostics, checkpoint, marks, history);
        Engine.VisitAdded += Visits.Add;
    }

    public LogTimeConverter Time { get; }

    public ManualClock Clock { get; }

    public FixedProcessProvider Processes { get; }

    public CollectingDiagnostics Diagnostics { get; }

    public HistoryEngine Engine { get; }

    public List<Core.Visits.VisitRecord> Visits { get; } = [];

    public DateTime Utc(DateTime local)
    {
        Time.TryToUtc(local, out var utc);
        return utc;
    }

    public void SetNow(DateTime local) => Clock.UtcNow = Utc(local);

    private void AdvanceTo(DateTime utc)
    {
        if (utc > Clock.UtcNow)
            Clock.UtcNow = utc;
    }

    public EngineSnapshot Snapshot() => Engine.Snapshot();

    public void Dispose() => Engine.Dispose();

    private static TimeZoneInfo ResolveTokyo()
    {
        foreach (var id in new[] { "Asia/Tokyo", "Tokyo Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // 次の候補を試す。
            }
        }

        return TimeZoneInfo.Local;
    }
}
