using System.Collections.Concurrent;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Tests;

/// <summary>検証用。指定したプロセスが動いていることにする。</summary>
public sealed class FixedProcessProvider(ClientProcessInfo? process) : IProcessProvider
{
    private readonly ConcurrentQueue<ProcessEvent> _events = new();
    private ClientProcessInfo? _current = process;
    private bool _announced;

    public IReadOnlyList<ClientProcessInfo> Running => _current is { } p ? [p] : [];

    public void Poll()
    {
        if (_announced || _current is not { } p)
            return;

        _announced = true;
        _events.Enqueue(new ProcessEvent(ProcessEventKind.Started, p, p.StartUtc, TimeSource.Process));
    }

    public void Exit(DateTime atUtc, TimeSource source = TimeSource.Process)
    {
        if (_current is not { } p)
            return;

        _current = null;
        _events.Enqueue(new ProcessEvent(ProcessEventKind.Exited, p, atUtc, source));
    }

    public void Start(ClientProcessInfo process, DateTime atUtc)
    {
        _current = process;
        _announced = true;
        _events.Enqueue(new ProcessEvent(ProcessEventKind.Started, process, atUtc, TimeSource.Process));
    }

    public bool TryDequeue(out ProcessEvent ev) => _events.TryDequeue(out ev);
}
