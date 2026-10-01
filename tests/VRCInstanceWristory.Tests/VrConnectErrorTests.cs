using VRCInstanceWristory.Core;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// SteamVR が動いているのに D3D11 デバイスを作れずにつなげないときの状態の表示（2026-09-28のユーザー指定→実装メモ5.82）。
/// </summary>
public class VrConnectErrorTests
{
    private static DesktopStatus Status(bool connected, VrConnectError error)
        => new() { ClientRunning = true, Health = LogHealth.Ok, VrConnected = connected, VrError = error };

    private static string VrText(DesktopStatus status) => StatusText.Build(status)[2].Value;

    [Fact]
    public void D3D11デバイスが枯渇してつなげないときは動作中と接続エラーを出す()
    {
        // 頭の語は 5.85 で「起動中・」から「動作中・」（SteamVR は動いているが、つながっていない）に揃えた。
        Assert.Equal("動作中・接続エラー (D3D11デバイス枯渇)", VrText(Status(false, VrConnectError.TextureDeviceExhausted)));
        Assert.Equal("動作中・接続エラー (D3D11デバイス)", VrText(Status(false, VrConnectError.TextureDevice)));
        Assert.Equal("未接続", VrText(Status(false, VrConnectError.None)));

        // つながっていれば、前の失敗は出さない。
        Assert.Equal("正常動作中", VrText(Status(true, VrConnectError.TextureDeviceExhausted)));
    }

    [Fact]
    public void コンポジターが止まっているときは赤で出す()
    {
        // 2026-09-29: vrserver は残ったままコンポジターが落ちていたのに「正常動作中」と出ていた（→実装メモ5.88）。
        var item = StatusText.Build(Status(false, VrConnectError.CompositorDown))[2];

        Assert.Equal("動作中・コンポジター停止", item.Value);
        Assert.Equal(StatusTone.Error, item.Tone);
        Assert.Contains("SteamVR を再起動", item.Detail);
    }

    [Fact]
    public void コンポジターがいない間はつなぎに行かない()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 29, 1, 8, 47, DateTimeKind.Utc));
        VrServerProcess? server = new VrServerProcess(100, clock.UtcNow);
        var compositor = false;
        var watcher = new SteamVrWatcher(() => server, () => compositor, clock);

        // 起動の途中（サーバーが先に立ち上がる）は、つながずに待つが「停止」とは出さない。
        Assert.False(watcher.ShouldConnectAtStartup());
        Assert.True(watcher.ServerSeen);
        Assert.False(watcher.CompositorDown);

        // 起動してしばらく経ってもいなければ「停止」。
        clock.Advance(SteamVrWatcher.CompositorStartGrace);
        Assert.False(watcher.ShouldConnect());
        Assert.True(watcher.CompositorDown);

        // 動き出したら、つなぎに行く。
        compositor = true;
        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.True(watcher.ShouldConnect());
        Assert.False(watcher.CompositorDown);
    }

    [Fact]
    public void つないでいる間にコンポジターが落ちたら見張りが知らせる()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 29, 1, 8, 47, DateTimeKind.Utc));
        VrServerProcess? server = new VrServerProcess(100, clock.UtcNow);
        var compositor = true;
        var watcher = new SteamVrWatcher(() => server, () => compositor, clock);

        Assert.True(watcher.ShouldConnectAtStartup());
        watcher.Connected();

        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.False(watcher.CompositorLost());

        // 見えなくなった直後は、SteamVR の終了の途中かもしれないので待つ。
        compositor = false;
        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.False(watcher.CompositorLost());

        clock.Advance(SteamVrWatcher.CompositorLostDelay);
        Assert.True(watcher.CompositorLost());

        // 切ったあとは「停止」と出し、同じサーバーでもコンポジターが戻ればつなぎ直す。
        watcher.Disconnected();
        clock.Advance(SteamVrWatcher.RetryInterval);
        Assert.False(watcher.ShouldConnect());
        Assert.True(watcher.CompositorDown);
        Assert.False(watcher.WaitingForExit);

        compositor = true;
        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.True(watcher.ShouldConnect());
    }

    [Fact]
    public void SteamVRのサーバーがいなくなったことを見張りから分かる()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc));
        VrServerProcess? server = new VrServerProcess(100, clock.UtcNow);
        var watcher = new SteamVrWatcher(() => server, () => true, clock);

        Assert.True(watcher.ShouldConnectAtStartup());
        Assert.True(watcher.ServerSeen);
        watcher.Failed();

        server = null;
        clock.Advance(SteamVrWatcher.CheckInterval);
        Assert.False(watcher.ShouldConnect());
        Assert.False(watcher.ServerSeen);
    }
}
