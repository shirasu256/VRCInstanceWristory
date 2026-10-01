using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 印の列に出す滞在時間の棒（2026-09-21のユーザー指定）。
/// どのインスタンスに長くいたかを、時刻を読まずに見分けるための印。
/// 長さは保持時間（<see cref="HistoryStore.DefaultRetention"/>・既定60分）で最大に達する。
/// </summary>
public class StayBarTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 2, 0, 0, DateTimeKind.Utc);

    private static readonly LogTimeConverter Time = new(EngineHarness.Tokyo);

    private static VisitRecord Record(int minutesAgo, int? leftMinutesAgo = null)
        => new()
        {
            EventId = $"s1@{minutesAgo}",
            SourceSessionId = "s1",
            SuccessByteOffset = minutesAgo,
            SessionOrder = 0,
            LocationKey = "wrld_x:07254",
            WorldId = "wrld_x",
            InstanceId = "07254",
            AccessType = AccessType.Public,
            WorldName = "ワールド",
            VisitedAtUtc = Now - TimeSpan.FromMinutes(minutesAgo),
            LeftAtUtc = leftMinutesAgo is { } left ? Now - TimeSpan.FromMinutes(left) : null,
            VisitOrdinal = 1,
        };

    [Fact]
    public void 棒の長さは保持時間と同じ60分で最大に達する()
    {
        Assert.Equal(60d, HistoryStore.DefaultRetention.TotalMinutes);

        Assert.Equal(0f, RowFormatter.StayFraction(Record(30, leftMinutesAgo: 30), Now));
        Assert.Equal(0.5f, RowFormatter.StayFraction(Record(50, leftMinutesAgo: 20), Now), 3);
        Assert.Equal(1f, RowFormatter.StayFraction(Record(60, leftMinutesAgo: 0), Now));

        // 60分を超えても頭打ちにする（滞在中の行は無期限に残るため）。
        Assert.Equal(1f, RowFormatter.StayFraction(Record(180, leftMinutesAgo: 0), Now));
    }

    /// <summary>滞在中の行は退出時刻がないので、入室から現在までで伸ばす。</summary>
    [Fact]
    public void 滞在中の行は入室から現在までで伸びる()
    {
        var record = Record(15);

        Assert.Equal(0.25f, RowFormatter.StayFraction(record, Now), 3);
        Assert.Equal(0.5f, RowFormatter.StayFraction(record, Now + TimeSpan.FromMinutes(15)), 3);
    }

    /// <summary>
    /// 1分単位に切り捨てる。滞在中でも値が変わるのは1分に1回までなので、
    /// 行の下絵の描き直しがそれ以上増えない。
    /// 切り捨てなので、退出した瞬間に棒が短くなることもない。
    /// </summary>
    [Fact]
    public void 滞在時間は1分単位に丸める()
    {
        var record = Record(3);

        var atThreeMinutes = RowFormatter.StayFraction(record, Now);
        var thirtySecondsLater = RowFormatter.StayFraction(record, Now + TimeSpan.FromSeconds(30));
        var oneMinuteLater = RowFormatter.StayFraction(record, Now + TimeSpan.FromMinutes(1));

        Assert.Equal(atThreeMinutes, thirtySecondsLater);
        Assert.True(oneMinuteLater > atThreeMinutes);

        // 1分に満たない滞在は0（＝丸）。負にはならない。
        Assert.Equal(0f, RowFormatter.StayFraction(Record(0), Now));
    }

    [Fact]
    public void 棒は丸から行の高さマイナス上下余白まで伸びる()
    {
        var style = new PanelStyle();

        // 滞在0分は太さぶんだけ＝直径 StayBarWidth の丸。
        Assert.Equal(style.StayBarWidth, style.StayBarLength(0f, style.RowHeight));

        // 最大でも行の高さから上下の余白を引いたぶんまで。
        var max = style.RowHeight - (style.StayBarPadding * 2f);
        Assert.Equal(max, style.StayBarLength(1f, style.RowHeight));
        Assert.Equal(max, style.StayBarLength(2f, style.RowHeight));
        Assert.True(max < style.RowHeight);

        // 間は割合の平方根で伸ばす（2026-09-25のユーザー指定）。半分の滞在（30分）で最大の7割ほど。
        Assert.Equal(style.StayBarWidth + ((max - style.StayBarWidth) * MathF.Sqrt(0.5f)), style.StayBarLength(0.5f, style.RowHeight), 3);

        // IDが折り返して高くなった行では、余白を保ったまま最大も伸びる。
        var tall = style.RowHeight * 1.5f;
        Assert.Equal(tall - (style.StayBarPadding * 2f), style.StayBarLength(1f, tall));
    }

    /// <summary>
    /// 長さは滞在時間に比例させず、平方根で伸ばす（2026-09-25のユーザー指定→実装メモ5.36）。
    /// 比例だと数分の滞在はどれも丸に近い長さに潰れて見分けが付かない。
    /// 平方根なら短い滞在どうしの差が広がり、60分で最大になるのは変わらない。
    /// </summary>
    [Fact]
    public void 短い滞在どうしの差は平方根で広げる()
    {
        var style = new PanelStyle();
        var max = style.RowHeight - (style.StayBarPadding * 2f);

        float Length(int minutes) => style.StayBarLength(minutes / 60f, style.RowHeight);
        float Linear(int minutes) => style.StayBarWidth + ((max - style.StayBarWidth) * (minutes / 60f));

        Assert.Equal(0.5f, style.StayBarGamma);

        // 1分で17.3px・3分で24.1px（比例では9.2px・11.6pxで、どちらも丸とほとんど変わらなかった）。
        Assert.Equal(17.3f, Length(1), 1);
        Assert.Equal(24.1f, Length(3), 1);

        // 1分と5分の差は、比例のときより大きい（短い滞在ほど伸ばす）。
        Assert.True(Length(5) - Length(1) > Linear(5) - Linear(1));

        // 長いほど長いことは変わらず、0分は丸・60分で最大のまま。
        Assert.True(Length(1) < Length(5) && Length(5) < Length(30) && Length(30) < Length(60));
        Assert.Equal(style.StayBarWidth, Length(0));
        Assert.Equal(max, Length(60));
    }

    [Fact]
    public void 棒は印の列の中にあり左右にはみ出さない()
    {
        var style = new PanelStyle();

        Assert.True(style.StayBarLeft >= style.PaddingLeft);
        Assert.True(style.StayBarLeft + style.StayBarWidth <= style.IdColumnLeft);

        // ▶ の見た目の中心に合わせて、列の中央よりわずかに左。
        Assert.Equal(style.MarkerColumnCenter + style.StayBarCenterAdjust, style.StayBarLeft + (style.StayBarWidth / 2f), 3);
        Assert.InRange(style.StayBarCenterAdjust, -6f, 0f);
    }

    /// <summary>実際に描いたとき、長く滞在した行ほど印の列の棒が縦に長くなる。</summary>
    [Fact]
    public void 長く滞在した行ほど棒が長く描かれる()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var rows = new List<DisplayRow>
        {
            RowFormatter.Build(Record(59, leftMinutesAgo: 58), Time, isCurrent: false, Now),   // 1分
            RowFormatter.Build(Record(58, leftMinutesAgo: 43), Time, isCurrent: false, Now),   // 15分
            RowFormatter.Build(Record(43, leftMinutesAgo: 0), Time, isCurrent: false, Now),    // 43分
        };

        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, 0f);

        var pixels = renderer.GetPixels();
        var x = (int)MathF.Round(style.StayBarLeft + (style.StayBarWidth / 2f));

        int BarHeight(int index)
        {
            var top = style.ViewportTop + (index * style.RowHeight);
            var count = 0;

            for (var y = top; y < top + style.RowHeight; y++)
            {
                // 背景より不透明な画素＝棒。棒は背景の上に重ねて描く。
                if (pixels[(((y * style.Width) + x) * 4) + 3] > style.BackgroundAlpha)
                    count++;
            }

            return count;
        }

        var shortStay = BarHeight(0);
        var middle = BarHeight(1);
        var longStay = BarHeight(2);

        Assert.True(shortStay > 0, "滞在1分の行にも丸が出る");
        Assert.True(middle > shortStay, $"15分（{middle}px）は1分（{shortStay}px）より長い");
        Assert.True(longStay > middle, $"43分（{longStay}px）は15分（{middle}px）より長い");

        // 上下の余白は残る。
        Assert.True(longStay <= style.RowHeight - (style.StayBarPadding * 2f));
    }

    /// <summary>
    /// ▶ の1/3からさらに半分へ下げた濃さ（2026-09-21のユーザー指定）。
    /// 行の内容を邪魔しない程度に薄くする。
    /// </summary>
    [Fact]
    public void 棒は印よりかなり薄い()
    {
        var style = new PanelStyle();

        Assert.Equal(0.165f, style.StayBarOpacity);
        Assert.Equal(42, style.StayBarAlpha);
        Assert.True(style.StayBarAlpha < 255);
    }

    /// <summary>
    /// 現在地の行は ▶ だけにする（2026-09-21のユーザー指定）。
    /// 棒と重ねると形が読み取りにくくなるため。
    /// </summary>
    [Fact]
    public void 現在地の行には棒を描かない()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        // 同じ滞在時間（60分＝最大の長さ）の行を、現在地とそうでない行で1つずつ描く。
        var rows = new List<DisplayRow>
        {
            RowFormatter.Build(Record(60, leftMinutesAgo: 0), Time, isCurrent: false, Now),
            RowFormatter.Build(Record(60), Time, isCurrent: true, Now),
        };

        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, 0f);

        var pixels = renderer.GetPixels();
        var x = (int)MathF.Round(style.MarkerColumnCenter);

        // 棒は行の上下の余白まで届くので、そこに印の画素があるかどうかで見分けられる。
        // ▶ は文字の高さ（26px強）ぶんしかないため、余白のすぐ内側には届かない。
        bool InkAt(int rowIndex, int offsetFromTop)
        {
            var y = style.ViewportTop + (rowIndex * style.RowHeight) + offsetFromTop;
            return pixels[(((y * style.Width) + x) * 4) + 3] > style.BackgroundAlpha;
        }

        Assert.True(InkAt(0, (int)style.StayBarPadding + 2), "滞在中でない行には棒が出る");
        Assert.False(InkAt(1, (int)style.StayBarPadding + 2), "現在地の行には棒を出さない");
    }

    /// <summary>
    /// ▶ と棒は、見た目の中心がそろって見えるように置く（2026-09-21のユーザー指定・指摘）。
    ///
    /// ▶ は右向きの三角形なので、字面の真ん中に置くと見た目は左寄りになる。
    /// 字面の幅ではなく、濃さで重みを付けた中心（＝見た目の中心）どうしを比べる。
    /// </summary>
    [Fact]
    public void 印と棒は見た目の中心がそろう()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var rows = new List<DisplayRow>
        {
            RowFormatter.Build(Record(60, leftMinutesAgo: 0), Time, isCurrent: false, Now),
            RowFormatter.Build(Record(60), Time, isCurrent: true, Now),
        };

        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, 0f);

        var pixels = renderer.GetPixels();

        // 印の列（16〜50px）で、背景より濃い画素の重心を求める。
        // 行の区切り線は列いっぱいに伸びて重心をずらすので、行の上下10pxは見ない。
        float InkCenter(int rowIndex)
        {
            var top = style.ViewportTop + (rowIndex * style.RowHeight);
            var total = 0f;
            var moment = 0f;

            for (var y = top + 10; y < top + style.RowHeight - 10; y++)
            {
                for (var x = style.PaddingLeft; x < style.IdColumnLeft; x++)
                {
                    var weight = pixels[(((y * style.Width) + x) * 4) + 3] - style.BackgroundAlpha;

                    if (weight <= 0)
                        continue;

                    total += weight;
                    moment += weight * (x + 0.5f);
                }
            }

            Assert.True(total > 0f, "印の列に何も描かれていない");
            return moment / total;
        }

        var bar = InkCenter(0);
        var marker = InkCenter(1);

        // 画素の丸めぶん（±1px）は許す。
        Assert.Equal(marker, bar, 1f);

        // 棒は列の中央より左にある（▶ の見た目の中心に合わせたため）。
        Assert.True(bar < style.MarkerColumnCenter, $"棒の中心 {bar} が列の中央 {style.MarkerColumnCenter} より左にない");
    }

    /// <summary>
    /// 滞在中の行の棒は時間とともに伸びるので、世代が変わらなくても作り直す必要がある。
    /// ただし1分単位に丸めてあるので、作り直すのは1分に1回まで
    /// （行の下絵の描き直しはGDI+で1回10msほどかかるため→実装メモ5.28・5.35）。
    /// </summary>
    [Fact]
    public void 滞在中の行は世代が同じでも1分ごとに作り直す()
    {
        var clock = new ManualClock(Now);
        var panel = new RecordingPanelTarget();
        var presenter = new PanelPresenter(panel, Time, NullDiagnostics.Instance, clock);

        var record = Record(3);

        EngineSnapshot Snapshot() => new()
        {
            ClientRunning = true,
            Presence = PresenceState.InTarget,
            Health = LogHealth.Ok,
            History = [record],
            CurrentEventId = record.EventId,
            MenuPageOpen = true,
            AwaitingViewAngleClose = false,
            BaselineKnown = true,
            CheckpointHealthy = true,
            Generation = 1,
        };

        presenter.Apply(Snapshot());
        Assert.Single(panel.RowUpdates);
        var first = panel.LastRows[0].StayFraction;

        // 30秒では丸めた値が変わらないので作り直さない。
        clock.Advance(TimeSpan.FromSeconds(30));
        presenter.Apply(Snapshot());
        Assert.Single(panel.RowUpdates);

        // 1分たてば棒が伸びるので作り直す。
        clock.Advance(TimeSpan.FromSeconds(30));
        presenter.Apply(Snapshot());
        Assert.Equal(2, panel.Updates);
        Assert.True(panel.LastRows[0].StayFraction > first);
    }
}
