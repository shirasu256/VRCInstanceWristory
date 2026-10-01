using VRCInstanceWristory.Core;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 検証で使う VRChat のプロセスと、現地時刻から UTC への直し方。
/// 現地時刻はどれも東京（<see cref="EngineHarness.Tokyo"/>）として読むので、検証を動かす PC の時間帯によらない。
/// 各ファイルでは <c>using static VRCInstanceWristory.Tests.TestProcesses;</c> として <c>Process(...)</c>・<c>Utc(...)</c> で呼ぶ。
/// </summary>
public static class TestProcesses
{
    private static readonly LogTimeConverter Time = new(EngineHarness.Tokyo);

    /// <summary>東京の現地時刻 <paramref name="startLocal"/> に起動した VRChat のプロセス。</summary>
    public static ClientProcessInfo Process(DateTime startLocal, int pid = 4242) => new(pid, Utc(startLocal));

    /// <summary>東京の現地時刻を UTC へ直す。</summary>
    public static DateTime Utc(DateTime local)
    {
        Time.TryToUtc(local, out var utc);
        return utc;
    }
}
