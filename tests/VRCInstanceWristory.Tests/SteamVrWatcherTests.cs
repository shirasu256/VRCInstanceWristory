using VRCInstanceWristory.Modes;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// SteamVRの起動を見張ってからつなぐ（2026-09-26のユーザー指定→実装メモ5.51・5.57）。
/// アプリを起動しただけで SteamVR を立ち上げないよう、SteamVR のサーバーが動いているのを確かめてからつなぐ。
/// 本物のプロセスは探さず、差し替えた「サーバーを探す」処理で確かめる。
/// </summary>
public class SteamVrWatcherTests
{
    private sealed class FakeServer
    {
        public VrServerProcess? Current { get; set; }

        public int Calls { get; private set; }

        public VrServerProcess? Find()
        {
            Calls++;
            return Current;
        }
    }

    [Fact]
    public void SteamVRのサーバーが動くまではつなぎに行かない()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc));
        var server = new FakeServer();
        var watcher = new SteamVrWatcher(server.Find, () => true, clock);

        Assert.False(watcher.ShouldConnect());

        // 見張りの間隔より前は探し直さない。
        server.Current = new VrServerProcess(100, clock.UtcNow);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(watcher.ShouldConnect());
        Assert.Equal(1, server.Calls);

        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.True(watcher.ShouldConnect());
    }

    /// <summary>アプリの側を起動したときに SteamVR まで立ち上げない（2026-09-27のユーザー指定→実装メモ5.57）。</summary>
    [Fact]
    public void アプリを起動した時点でSteamVRが動いていなければつなぎに行かず_起動したらつなぐ()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc));
        var server = new FakeServer();
        var watcher = new SteamVrWatcher(server.Find, () => true, clock);

        Assert.False(watcher.ShouldConnectAtStartup());

        // SteamVR が起動したら、見張りがつなぐ。
        server.Current = new VrServerProcess(100, clock.UtcNow);
        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.True(watcher.ShouldConnect());
    }

    [Fact]
    public void 起動した時点でつなげなかったら_同じサーバーには間を置いて試し直す()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc));
        var server = new FakeServer { Current = new VrServerProcess(100, clock.UtcNow) };
        var watcher = new SteamVrWatcher(server.Find, () => true, clock);

        Assert.True(watcher.ShouldConnectAtStartup());
        watcher.Failed();

        // すぐには試し直さない（起動の途中でまだ受け付けないことがある）。
        Assert.False(watcher.ShouldConnect());
        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.False(watcher.ShouldConnect());

        clock.Advance(SteamVrWatcher.RetryInterval);
        Assert.True(watcher.ShouldConnect());
    }

    [Fact]
    public void 一度つないだサーバーには終了のあとつなぎ直さず_新しく起動したサーバーにはつなぐ()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc));
        var server = new FakeServer { Current = new VrServerProcess(100, new DateTime(2026, 9, 26, 11, 0, 0, DateTimeKind.Utc)) };
        var watcher = new SteamVrWatcher(server.Find, () => true, clock);

        Assert.True(watcher.ShouldConnect());
        watcher.Connected();

        // SteamVR を終了しても、同じサーバーがしばらく残っていることがある。そこへはつながない。
        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.False(watcher.ShouldConnect());

        // 起動し直したサーバー（別のプロセス）にはつなぐ。
        server.Current = new VrServerProcess(200, clock.UtcNow);
        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.True(watcher.ShouldConnect());
    }

    [Fact]
    public void つなげなかったら間を置いて試し直し_2つ目の起動があればすぐ試す()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc));
        var server = new FakeServer { Current = new VrServerProcess(100, clock.UtcNow) };
        var watcher = new SteamVrWatcher(server.Find, () => true, clock);

        Assert.True(watcher.ShouldConnect());
        watcher.Failed();

        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.False(watcher.ShouldConnect());

        clock.Advance(SteamVrWatcher.RetryInterval);
        Assert.True(watcher.ShouldConnect());
        watcher.Failed();

        watcher.Poke();
        Assert.True(watcher.ShouldConnect());
    }
}
