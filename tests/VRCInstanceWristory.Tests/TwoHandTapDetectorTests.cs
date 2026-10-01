using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 2026-09-30の指定。VRChatのメインメニューは、B を押し続けている間に Y を短押ししても
/// （その逆でも）閉じる。左右の押下を1つにまとめると押し続けている側で押下が途切れず、
/// 短押しを取りこぼしていた（→実装メモ5.96）。
/// </summary>
public class TwoHandTapDetectorTests
{
    private static TwoHandTapDetector Detector() => new(OverlayController.CloseTapMaxHold);

    private static TimeSpan Ms(double value) => TimeSpan.FromMilliseconds(value);

    [Fact]
    public void 片手ずつの短押しはこれまでどおり拾う()
    {
        var tap = Detector();

        Assert.False(tap.Update(false, true, Ms(0)));
        Assert.True(tap.Update(false, false, Ms(80)));

        Assert.False(tap.Update(true, false, Ms(500)));
        Assert.True(tap.Update(false, false, Ms(580)));
    }

    [Fact]
    public void Bを押し続けたままYを短押しすると閉じる()
    {
        var tap = Detector();

        // 右手の B を押し続ける。長押しなので、これだけでは成立しない。
        Assert.False(tap.Update(false, true, Ms(0)));
        Assert.False(tap.Update(false, true, Ms(400)));

        // その間に左手の Y を短押しする。
        Assert.False(tap.Update(true, true, Ms(500)));
        Assert.True(tap.Update(false, true, Ms(560)));

        // B を離しても、長押しなので重ねて成立しない。
        Assert.False(tap.Update(false, false, Ms(900)));
    }

    [Fact]
    public void Yを押し続けたままBを短押ししても閉じる()
    {
        var tap = Detector();

        Assert.False(tap.Update(true, false, Ms(0)));
        Assert.False(tap.Update(true, false, Ms(400)));

        Assert.False(tap.Update(true, true, Ms(500)));
        Assert.True(tap.Update(true, false, Ms(560)));

        Assert.False(tap.Update(false, false, Ms(900)));
    }

    [Fact]
    public void 押し続けている側を離し直さなくても短押しを繰り返し拾う()
    {
        var tap = Detector();

        tap.Update(false, true, Ms(0));
        tap.Update(false, true, Ms(400));

        tap.Update(true, true, Ms(500));
        Assert.True(tap.Update(false, true, Ms(560)));

        tap.Update(true, true, Ms(800));
        Assert.True(tap.Update(false, true, Ms(860)));
    }

    [Fact]
    public void 両手とも長押しなら成立しない()
    {
        var tap = Detector();

        tap.Update(true, true, Ms(0));
        tap.Update(true, true, Ms(400));
        Assert.False(tap.Update(false, false, Ms(420)));
    }

    [Fact]
    public void 同じフレームで両手が成立しても1回として返す()
    {
        var tap = Detector();

        tap.Update(true, true, Ms(0));
        Assert.True(tap.Update(false, false, Ms(60)));
        Assert.False(tap.Update(false, false, Ms(70)));
    }

    [Fact]
    public void Resetすると両手の押下の追跡を捨てる()
    {
        var tap = Detector();

        tap.Update(true, true, Ms(0));
        tap.Reset();

        Assert.False(tap.Update(false, false, Ms(50)));
    }
}
