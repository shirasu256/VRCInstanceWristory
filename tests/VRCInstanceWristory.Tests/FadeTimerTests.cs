using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 2026-09-19の指定。視線角度の条件が切り替わった時点から0.2秒かけて表示・非表示を渡す
/// （角度そのものを不透明度へ写す案は実機で取りやめた）。
/// </summary>
public class FadeTimerTests
{
    private static FadeTimer Fade() => new(TimeSpan.FromSeconds(0.2));

    [Fact]
    public void 規定時間をかけて薄くなり最後に消える()
    {
        var fade = Fade();
        Assert.Equal(1f, fade.Alpha);

        Assert.Equal(0.5f, fade.Update(false, 0.1f), 4);
        Assert.True(fade.Visible);

        Assert.Equal(0.25f, fade.Update(false, 0.05f), 4);

        // 0.2秒ぶん進めば消える。行き過ぎて負にはならない。
        Assert.Equal(0f, fade.Update(false, 0.5f));
        Assert.False(fade.Visible);
    }

    [Fact]
    public void 戻るときも同じ時間で濃くなる()
    {
        var fade = Fade();
        fade.Snap(false);

        Assert.Equal(0.5f, fade.Update(true, 0.1f), 4);
        Assert.True(fade.Visible); // 濃くなり始めた時点でもう出してよい。

        Assert.Equal(1f, fade.Update(true, 0.1f), 4);
        Assert.Equal(1f, fade.Update(true, 1f)); // 1 を超えない。
    }

    [Fact]
    public void 途中で向きが変わればその場から戻る()
    {
        var fade = Fade();

        fade.Update(false, 0.1f);
        Assert.Equal(0.5f, fade.Alpha, 4);

        // 消えきる前に規定内へ戻したら、0 まで落ちずにその場から濃くなる。
        Assert.Equal(0.75f, fade.Update(true, 0.05f), 4);
    }

    [Fact]
    public void 別の理由で隠れている間は途中を残さない()
    {
        var fade = Fade();
        fade.Update(false, 0.1f);

        fade.Snap(true);
        Assert.Equal(1f, fade.Alpha);

        fade.Snap(false);
        Assert.Equal(0f, fade.Alpha);
    }

    [Fact]
    public void 時間を0にすると一瞬で切り替わる()
    {
        var fade = new FadeTimer(TimeSpan.Zero);

        Assert.Equal(0f, fade.Update(false, 0f));
        Assert.Equal(1f, fade.Update(true, 0f));
    }

    [Fact]
    public void 経過時間が異常でも壊れない()
    {
        var fade = Fade();

        Assert.Equal(1f, fade.Update(true, float.NaN));
        Assert.Equal(1f, fade.Update(true, -5f));

        // 負の経過で勝手に薄くならない。
        Assert.Equal(1f, fade.Update(false, -5f));
    }
}
