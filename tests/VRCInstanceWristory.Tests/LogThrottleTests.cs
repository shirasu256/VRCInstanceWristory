using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// SteamVR への呼び出しの失敗のログの間引き。失敗しても毎フレーム（90Hz）試し直すので、そのたびに出すとログが埋まる。
/// </summary>
public class LogThrottleTests
{
    [Fact]
    public void 最初の失敗はすぐに出し_間隔の内側は数えるだけで_間隔を過ぎたら回数を添えて出す()
    {
        var throttle = new LogThrottle(TimeSpan.FromSeconds(5));
        var now = TimeSpan.FromSeconds(100);
        var frame = TimeSpan.FromMilliseconds(1000.0 / 90.0);

        Assert.True(throttle.ShouldLog("SetOverlayTransformTrackedDeviceRelative", now, out var suppressed));
        Assert.Equal(0, suppressed);

        // 90Hzで4秒ぶん失敗し続けても出さない。
        var skipped = 0;
        for (var t = now + frame; t < now + TimeSpan.FromSeconds(4); t += frame)
        {
            Assert.False(throttle.ShouldLog("SetOverlayTransformTrackedDeviceRelative", t, out _));
            skipped++;
        }

        Assert.True(throttle.ShouldLog("SetOverlayTransformTrackedDeviceRelative", now + TimeSpan.FromSeconds(5), out suppressed));
        Assert.Equal(skipped, suppressed);

        // 数えた回数は出したら0へ戻る。
        Assert.True(throttle.ShouldLog("SetOverlayTransformTrackedDeviceRelative", now + TimeSpan.FromSeconds(10), out suppressed));
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void 種類ごとに別々に間引く()
    {
        var throttle = new LogThrottle(TimeSpan.FromSeconds(5));
        var now = TimeSpan.FromSeconds(1);

        Assert.True(throttle.ShouldLog("ShowOverlay", now, out _));
        Assert.False(throttle.ShouldLog("ShowOverlay", now, out _));

        // ほかの操作の最初の失敗は、すぐに出す。
        Assert.True(throttle.ShouldLog("SetOverlayAlpha", now, out _));
    }
}
