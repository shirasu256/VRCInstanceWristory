using System.Collections.Concurrent;
using System.Diagnostics;
using VRCInstanceWristory.Core.Counting;

namespace VRCInstanceWristory.Infrastructure;

/// <param name="StartUtc">プロセス開始時刻。PID再利用を避けるため識別に含める。</param>
public readonly record struct ClientProcessInfo(int Pid, DateTime StartUtc);

public enum ProcessEventKind
{
    Started,
    Exited,
}

/// <param name="AtUtc">開始または終了の時刻。</param>
/// <param name="Source">時刻の出典。通知・存否確認だけで得た時刻は Observed。</param>
public readonly record struct ProcessEvent(ProcessEventKind Kind, ClientProcessInfo Process, DateTime AtUtc, TimeSource Source);

/// <summary>
/// VRChat.exe の存否・PID・開始終了時刻だけを見る（仕様7節）。
/// プロセスメモリやウィンドウ内容は読まない。訪問情報はログだけから得る。
/// </summary>
public interface IProcessProvider
{
    /// <summary>存否を確認し、変化をイベントとして積む。</summary>
    void Poll();

    IReadOnlyList<ClientProcessInfo> Running { get; }

    bool TryDequeue(out ProcessEvent ev);

    /// <summary>直近の確認で、プロセスの一覧そのものを取れなかったか（→実装メモ5.85）。</summary>
    bool ListFailing => false;

    /// <summary>
    /// 見つけた VRChat のプロセスの情報（起動時刻）を読めないことが続いているか（→実装メモ5.85）。
    /// 読めないプロセスは <see cref="Running"/> に入らないので、VRChat が動いていないように見える。
    /// </summary>
    bool InfoUnreadable => false;
}

public sealed class VRChatProcessMonitor(string processName = "VRChat", IDiagnostics? diagnostics = null)
    : IProcessProvider, IDisposable
{
    private readonly IDiagnostics _log = diagnostics ?? NullDiagnostics.Instance;
    private readonly ConcurrentQueue<ProcessEvent> _events = new();
    private readonly Dictionary<int, Tracked> _tracked = [];

    /// <summary>
    /// 情報を読めないことがこの回数（＝確認の回数。1秒に1回）続いたら知らせる（→実装メモ5.85）。
    /// 一覧を取った直後にプロセスが終わるなど、一瞬だけ読めないことはふつうにあるため。
    /// </summary>
    private const int UnreadableStreakLimit = 5;

    private int _unreadableStreak;

    public bool ListFailing { get; private set; }

    public bool InfoUnreadable => _unreadableStreak >= UnreadableStreakLimit;

    public IReadOnlyList<ClientProcessInfo> Running { get; private set; } = [];

    public void Poll()
    {
        var seen = new HashSet<int>();
        var running = new List<ClientProcessInfo>();

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch (Exception ex)
        {
            _log.Warn($"プロセス一覧を取得できません: {ex.Message}");
            ListFailing = true;
            return;
        }

        ListFailing = false;
        var unreadable = false;

        foreach (var process in processes)
        {
            DateTime startUtc;
            int pid;

            try
            {
                pid = process.Id;
                startUtc = process.StartTime.ToUniversalTime();
            }
            catch (Exception ex)
            {
                // 一時的に読めないことがある。既知のPIDなら前回の情報を使い続け、
                // 「終了した」と誤判定してパネルを消さない。
                var recovered = false;

                try
                {
                    pid = process.Id;

                    if (_tracked.TryGetValue(pid, out var known))
                    {
                        seen.Add(pid);
                        running.Add(known.Info);
                        recovered = true;
                    }
                }
                catch (Exception)
                {
                    // PIDすら読めない場合は諦める。
                }

                if (!recovered)
                {
                    unreadable = true;

                    // 続くときは1秒ごとに同じ行が並ぶので、最初の1回と知らせる境目でだけ記録する。
                    if (_unreadableStreak == 0 || _unreadableStreak + 1 == UnreadableStreakLimit)
                        _log.Warn($"プロセス情報を読めません: {ex.Message}");
                }

                process.Dispose();
                continue;
            }

            seen.Add(pid);
            var info = new ClientProcessInfo(pid, startUtc);
            running.Add(info);

            if (_tracked.ContainsKey(pid))
            {
                process.Dispose();
                continue;
            }

            var tracked = new Tracked(process, info);
            _tracked[pid] = tracked;

            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => OnExited(tracked);
            }
            catch (Exception ex)
            {
                // 通知を張れなくても、存否の再確認で終了を検出する。
                _log.Info($"終了通知を登録できません（存否確認で代替）: {ex.Message}");
            }

            _events.Enqueue(new ProcessEvent(ProcessEventKind.Started, info, startUtc, TimeSource.Process));
        }

        foreach (var pid in _tracked.Keys.ToList())
        {
            if (seen.Contains(pid))
                continue;

            var tracked = _tracked[pid];
            OnExited(tracked);
            tracked.Dispose();
            _tracked.Remove(pid);
        }

        Running = running;
        _unreadableStreak = unreadable ? _unreadableStreak + 1 : 0;
    }

    /// <summary>
    /// 終了を1回だけ積む。スレッドプールの <see cref="Process.Exited"/> と、主ループの <see cref="Poll"/>（一覧から消えた）の
    /// どちらからも呼ばれるので、「もう積んだか」の確かめと印付けを1つの操作で行う。
    /// </summary>
    private void OnExited(Tracked tracked)
    {
        if (!tracked.TryMarkExitReported())
            return;

        var at = DateTime.UtcNow;
        var source = TimeSource.Observed;

        try
        {
            at = tracked.Process.ExitTime.ToUniversalTime();
            source = TimeSource.Process;
        }
        catch (Exception)
        {
            // 実際の終了時刻を取得できない場合は、確認時刻として扱う。
        }

        _events.Enqueue(new ProcessEvent(ProcessEventKind.Exited, tracked.Info, at, source));
    }

    public bool TryDequeue(out ProcessEvent ev) => _events.TryDequeue(out ev);

    public void Dispose()
    {
        foreach (var tracked in _tracked.Values)
            tracked.Dispose();

        _tracked.Clear();
    }

    private sealed class Tracked(Process process, ClientProcessInfo info) : IDisposable
    {
        public Process Process { get; } = process;

        public ClientProcessInfo Info { get; } = info;

        private int _exitReported;

        /// <summary>初めて呼んだときだけ true（別々のスレッドから同時に呼ばれても1回だけ）。</summary>
        public bool TryMarkExitReported() => Interlocked.Exchange(ref _exitReported, 1) == 0;

        public void Dispose() => Process.Dispose();
    }
}
