using System.Diagnostics;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 別のスレッドや外のプロセスが起こすことを、立つまで短い間隔で確かめ直して待つ。
/// 一定時間だけ眠ってから確かめると、PC が混んでいるときに間に合わずに落ちる。
/// </summary>
public static class Eventually
{
    /// <summary>既定の待ち時間。立つまでしか待たないので、長めにとっても通るときの速さは変わらない。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);

    /// <summary>条件が立つまで待つ。時間切れのときは、最後にもう一度確かめた結果を返す。</summary>
    public static bool True(Func<bool> condition, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        var limit = timeout ?? DefaultTimeout;

        while (watch.Elapsed < limit)
        {
            if (condition())
                return true;

            Thread.Sleep(Interval);
        }

        return condition();
    }

    /// <summary><see cref="True"/> のスレッドを塞がない版。</summary>
    public static async Task<bool> TrueAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        var limit = timeout ?? DefaultTimeout;

        while (watch.Elapsed < limit)
        {
            if (condition())
                return true;

            await Task.Delay(Interval);
        }

        return condition();
    }
}
