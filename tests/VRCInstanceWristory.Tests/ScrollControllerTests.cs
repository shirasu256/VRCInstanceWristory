using VRCInstanceWristory.Core.Scrolling;

namespace VRCInstanceWristory.Tests;

/// <summary>仕様8.2・8.3節（スクロール）と受入項目 T19・T20・T21・T28・T30。</summary>
public class ScrollControllerTests
{
    private const float RowHeight = 112f;

    private static ScrollController Create(int rows)
    {
        var scroll = new ScrollController
        {
            RowHeight = RowHeight,
            ViewportHeight = RowHeight * 7,
        };

        scroll.SetRows(Ids(rows));
        scroll.ResetToTail();
        return scroll;
    }

    private static List<string> Ids(int count, int from = 0)
        => Enumerable.Range(from, count).Select(i => $"e{i}").ToList();

    [Fact]
    public void T21_7行以下ならスクロールしない()
    {
        var scroll = Create(7);

        Assert.Equal(0f, scroll.MaxOffset);
        Assert.Equal(0f, scroll.Offset);
    }

    [Fact]
    public void T21_9行では末尾の7行から始まり上へ戻れる()
    {
        var scroll = Create(9);

        Assert.Equal(RowHeight * 2, scroll.Offset, 1);
        Assert.Equal(2, scroll.TopRowIndex);

        // 上へ倒すと古い側へ進む。
        scroll.Update(0f, active: true, deltaSeconds: 0f); // 中立へ戻す
        scroll.Update(1f, active: true, deltaSeconds: 1f);

        Assert.Equal(0f, scroll.Offset, 1);
        Assert.Equal(0, scroll.TopRowIndex);
    }

    [Fact]
    public void T20_デッドゾーン内では動かない()
    {
        var scroll = Create(20);
        var before = scroll.Offset;

        scroll.Update(0.2f, active: true, deltaSeconds: 1f);

        Assert.Equal(before, scroll.Offset, 3);
    }

    [Fact]
    public void T20_倒し量に比例し最大は毎秒6行()
    {
        var scroll = Create(30);
        scroll.Update(0f, active: true, deltaSeconds: 0f); // 中立へ戻す
        scroll.Update(1f, active: true, deltaSeconds: 1f);

        var moved = scroll.MaxOffset - scroll.Offset;
        Assert.Equal(RowHeight * 6f, moved, 1);
    }

    [Fact]
    public void T20_フレームレートに依存しない()
    {
        var a = Create(40);
        a.Update(0f, active: true, deltaSeconds: 0f);
        a.Update(1f, active: true, deltaSeconds: 0.5f);

        var b = Create(40);
        b.Update(0f, active: true, deltaSeconds: 0f);
        for (var i = 0; i < 50; i++)
            b.Update(1f, active: true, deltaSeconds: 0.01f);

        Assert.Equal(a.Offset, b.Offset, 1);
    }

    [Fact]
    public void T20_範囲外へは動かない()
    {
        var scroll = Create(9);

        scroll.Update(0f, active: true, deltaSeconds: 0f); // 中立へ戻す
        scroll.Update(1f, active: true, deltaSeconds: 10f);
        Assert.Equal(0f, scroll.Offset, 1);

        scroll.Update(-1f, active: true, deltaSeconds: 10f);
        Assert.Equal(scroll.MaxOffset, scroll.Offset, 1);
    }

    [Fact]
    public void T19_非命中では動かず次の命中は中立から始まる()
    {
        var scroll = Create(20);
        var before = scroll.Offset;

        // 命中していない間は積分しない。
        scroll.Update(1f, active: false, deltaSeconds: 5f);
        Assert.Equal(before, scroll.Offset, 3);

        // 命中開始時に倒れていたら、中立へ戻るまで受け付けない。
        scroll.Update(1f, active: true, deltaSeconds: 1f);
        Assert.Equal(before, scroll.Offset, 3);

        scroll.Update(0f, active: true, deltaSeconds: 0.1f); // 中立へ戻す
        scroll.Update(1f, active: true, deltaSeconds: 1f);
        Assert.True(scroll.Offset < before);
    }

    [Fact]
    public void T28_障害中の経過時間で一気にスクロールしない()
    {
        var scroll = Create(30);
        var before = scroll.Offset;

        // 追跡消失（非活性）が10秒続いた後、同じ姿勢で復帰する。
        for (var i = 0; i < 100; i++)
            scroll.Update(1f, active: false, deltaSeconds: 0.1f);

        scroll.Update(1f, active: true, deltaSeconds: 0.016f);

        Assert.Equal(before, scroll.Offset, 3);
    }

    [Fact]
    public void T30_末尾を見ているときだけ新着へ追従する()
    {
        var scroll = Create(9);
        Assert.True(scroll.FollowTail);

        scroll.SetRows(Ids(10));
        Assert.True(scroll.FollowTail);
        Assert.Equal(scroll.MaxOffset, scroll.Offset, 1);

        // 過去を見ている間は新着で飛ばさない。
        scroll.Update(0f, active: true, deltaSeconds: 0f); // 中立へ戻す
        scroll.Update(1f, active: true, deltaSeconds: 1f);
        var offset = scroll.Offset;
        Assert.False(scroll.FollowTail);

        scroll.SetRows(Ids(11));
        Assert.Equal(offset, scroll.Offset, 1);
    }

    [Fact]
    public void 期限切れで上の行が消えても見えている行を保つ()
    {
        var scroll = Create(12);

        // 先頭を3行目に合わせる。
        scroll.Update(0f, active: true, deltaSeconds: 0f); // 中立へ戻す
        scroll.Update(1f, active: true, deltaSeconds: 0.5f);
        var topId = $"e{scroll.TopRowIndex}";
        Assert.False(scroll.FollowTail);

        // 古い2行が期限切れで消える。
        scroll.SetRows(Ids(10, from: 2));

        Assert.Equal(topId, $"e{scroll.TopRowIndex + 2}");
    }

    [Fact]
    public void 基準行が消えたら直後の残存行に合わせる()
    {
        var scroll = Create(12);
        scroll.Update(0f, active: true, deltaSeconds: 0f); // 中立へ戻す
        scroll.Update(1f, active: true, deltaSeconds: 0.6f);

        var topIndex = scroll.TopRowIndex;

        // 見えていた先頭行そのものが消える。
        var remaining = Ids(12).Where((_, i) => i != topIndex).ToList();
        scroll.SetRows(remaining);

        Assert.Equal($"e{topIndex + 1}", remaining[scroll.TopRowIndex]);
    }

    [Fact]
    public void 対象外から戻るときは末尾へ戻す()
    {
        var scroll = Create(20);
        scroll.Update(0f, active: true, deltaSeconds: 0f); // 中立へ戻す
        scroll.Update(1f, active: true, deltaSeconds: 1f);
        Assert.False(scroll.FollowTail);

        scroll.ResetToTail();

        Assert.True(scroll.FollowTail);
        Assert.Equal(scroll.MaxOffset, scroll.Offset, 1);
    }

    [Fact]
    public void 長いIDで行が高くなっても内容の高さに反映する()
    {
        var scroll = new ScrollController
        {
            RowHeight = RowHeight,
            ViewportHeight = RowHeight * 7,
        };

        scroll.SetRows([
            new ScrollRow("a", RowHeight),
            new ScrollRow("b", RowHeight * 2),
            new ScrollRow("c", RowHeight),
        ]);

        Assert.Equal(RowHeight * 4, scroll.ContentHeight, 1);
    }
}
