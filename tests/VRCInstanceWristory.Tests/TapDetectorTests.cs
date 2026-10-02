using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 2026-09-21の指定。VRChatのメインメニューは B / Y を長押ししても閉じないため、
/// パネルを消す合図は短押しに限る。しきい値は2026-09-22に0.2秒から0.15秒、2026-10-01に0.1秒へ縮め、2026-10-02に0.2秒へ戻した。
/// 実際に使うしきい値（<see cref="OverlayController.CloseTapMaxHold"/>）で確かめる。
/// </summary>
public class TapDetectorTests
{
    private static TapDetector Detector() => new(OverlayController.CloseTapMaxHold);

    private static TimeSpan Threshold => OverlayController.CloseTapMaxHold;

    private static TimeSpan Ms(double value) => TimeSpan.FromMilliseconds(value);

    [Fact]
    public void しきい値の内側で離せば短押しになる()
    {
        var tap = Detector();

        // 押している間は成立しない。離した瞬間に一度だけ true。
        Assert.False(tap.Update(true, Ms(0)));
        Assert.False(tap.Update(true, Ms(50)));
        Assert.True(tap.Update(false, Ms(90)));

        // 離したままでは繰り返さない。
        Assert.False(tap.Update(false, Ms(100)));
    }

    [Fact]
    public void ちょうどしきい値までは短押しとみなす()
    {
        var tap = Detector();

        tap.Update(true, Ms(0));
        Assert.True(tap.Update(false, Threshold));
    }

    [Fact]
    public void しきい値を少しでも越えて離したら成立しない()
    {
        var tap = Detector();

        tap.Update(true, Ms(0));
        tap.Update(true, Threshold);
        Assert.False(tap.Update(false, Threshold + Ms(11)));

        // 押している間の更新がなく、離したときに初めて越えていたと分かる場合。
        tap.Update(true, Ms(1000));
        Assert.False(tap.Update(false, Ms(1000) + Threshold + Ms(20)));
    }

    [Fact]
    public void 押下180ミリ秒でも短押しになる()
    {
        var tap = Detector();

        // 0.1秒だったころ（2026-10-01〜02）は成立しなかった長さ。2026-10-02に0.2秒へ戻した。
        tap.Update(true, Ms(0));
        Assert.True(tap.Update(false, Ms(180)));
    }

    [Fact]
    public void しきい値を越えて押し続けたら離しても成立しない()
    {
        var tap = Detector();

        Assert.False(tap.Update(true, Ms(0)));
        Assert.False(tap.Update(true, Ms(300)));
        Assert.False(tap.Update(false, Ms(320)));
    }

    [Fact]
    public void 長押しの直後でも次の短押しは拾える()
    {
        var tap = Detector();

        tap.Update(true, Ms(0));
        tap.Update(true, Ms(1000));
        Assert.False(tap.Update(false, Ms(1010)));

        tap.Update(true, Ms(1200));
        Assert.True(tap.Update(false, Ms(1250)));
    }

    [Fact]
    public void 更新1回ぶんの押下でも短押しになる()
    {
        var tap = Detector();

        // 90Hzの更新で1フレームだけ押されて見えた場合。
        Assert.False(tap.Update(true, Ms(0)));
        Assert.True(tap.Update(false, Ms(11)));
    }

    [Fact]
    public void Resetすると押下の追跡を捨てる()
    {
        var tap = Detector();

        tap.Update(true, Ms(0));
        tap.Reset();

        // 離しても、押し始めを見ていないので成立しない。
        Assert.False(tap.Update(false, Ms(50)));
    }
}
