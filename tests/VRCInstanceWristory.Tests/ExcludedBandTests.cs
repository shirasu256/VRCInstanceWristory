using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 行と行の間に出す「∧ 対象外のインスタンスへ移動 ∨」の帯
/// （2026-09-21のユーザー指定、条件と文言は2026-09-26に変更→実装メモ5.38）。
///
/// 前の行を離れてから次の行へ入るまでに、対象外のインスタンスへ一瞬でも移っていれば、
/// その前後の行の間に帯を入れる。以前は「30分以上離れていたら」だったが、時間の長さは問わなくなった。
/// </summary>
public class ExcludedBandTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    private static readonly LogTimeConverter Time = new(EngineHarness.Tokyo);

    private static VisitRecord Record(int minutesAgo, int? leftMinutesAgo = null, string id = "07254", bool excludedBefore = false)
        => new()
        {
            EventId = $"s1@{id}-{minutesAgo}",
            SourceSessionId = "s1",
            SuccessByteOffset = minutesAgo,
            SessionOrder = 0,
            LocationKey = $"wrld_x:{id}",
            WorldId = "wrld_x",
            InstanceId = id,
            AccessType = AccessType.Public,
            WorldName = "ワールド",
            VisitedAtUtc = Now - TimeSpan.FromMinutes(minutesAgo),
            LeftAtUtc = leftMinutesAgo is { } left ? Now - TimeSpan.FromMinutes(left) : null,
            VisitOrdinal = 1,
            ExcludedBefore = excludedBefore,
        };

    /// <summary>直前の間隔を60分以上空けて、回数の基準を確定させる。</summary>
    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    private static List<DisplayRow> Rows(EngineHarness harness)
    {
        var snapshot = harness.Snapshot();
        return RowFormatter.Build(snapshot.History, snapshot.CurrentEventId, harness.Time, harness.Clock.UtcNow);
    }

    /// <summary>
    /// 対象外（Friends）に1分だけ寄ってから対象へ戻っても帯が出る。滞在の長さは問わない。
    /// 対象どうしを直接渡った区間には出さない。
    /// </summary>
    [Fact]
    public void 対象外へ一瞬でも移ったら帯が出る()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(2), Loc.Public("222"), "B")
            + LogText.Move(SessionStart.AddMinutes(3), Loc.Friends("333"), "C")
            + LogText.Move(SessionStart.AddMinutes(4), Loc.GroupOnly("444"), "D"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(10), Process(SessionStart));
        harness.Engine.Initialize();

        var rows = Rows(harness);

        Assert.Equal(["111", "222", "444"], rows.Select(r => r.InstanceId));
        Assert.False(rows[0].ExcludedBefore);

        // 対象から対象へ直接移った区間には出さない。
        Assert.False(rows[1].ExcludedBefore);

        // 222 → Friends（1分）→ 444。
        Assert.True(rows[2].ExcludedBefore);
        Assert.Equal("∧ 対象外のインスタンスへ移動 ∨", string.Concat(RowFormatter.BandSegments(rows[2].CrashedBefore, rows[2].ExcludedBefore).Select(s => s.Text)));
    }

    /// <summary>
    /// 対象外へ移って終了し、次の起動で直接対象へ入った場合も、前後の行の間に帯が出る。
    /// 前のセッションの行に付けた「離れたあと対象外へ移った」印で分かる。
    /// </summary>
    [Fact]
    public void 対象外へ移ったまま終了して次の起動で対象へ入っても帯が出る()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(2), Loc.Invite("222"), "B")
            + LogText.Quit(SessionStart.AddMinutes(3)));

        var restart = SessionStart.AddMinutes(5);
        dir.WriteSession(restart, LogText.Visit(restart.AddMinutes(1), Loc.Public("333"), "C"));

        using var harness = new EngineHarness(dir.Path, restart.AddMinutes(3), Process(restart));
        harness.Engine.Initialize();

        var rows = Rows(harness);

        Assert.Equal(["111", "333"], rows.Select(r => r.InstanceId));
        Assert.True(rows[1].ExcludedBefore);
        Assert.False(rows[1].CrashedBefore);
    }

    /// <summary>
    /// 起動してすぐ対象外（ホームなど）へ入り、そのあと対象へ入った場合も、
    /// 前のセッションの最後の行との間に帯が出る。
    /// </summary>
    [Fact]
    public void 起動直後に対象外を経由して対象へ入っても帯が出る()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Quit(SessionStart.AddMinutes(2)));

        var restart = SessionStart.AddMinutes(5);
        dir.WriteSession(
            restart,
            LogText.Visit(restart.AddMinutes(1), Loc.FriendsPlus("222"), "Home")
            + LogText.Move(restart.AddMinutes(2), Loc.Public("333"), "C"));

        using var harness = new EngineHarness(dir.Path, restart.AddMinutes(3), Process(restart));
        harness.Engine.Initialize();

        var rows = Rows(harness);

        Assert.Equal(["111", "333"], rows.Select(r => r.InstanceId));
        Assert.True(rows[1].ExcludedBefore);
    }

    /// <summary>
    /// 対象外を経由しなければ、どれだけ間が空いても帯は出さない。
    /// 以前の「30分以上離れていたら」の条件は使わない。
    /// </summary>
    [Fact]
    public void 対象外を経由しなければ間が空いても出さない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Quit(SessionStart.AddMinutes(2)));

        var restart = SessionStart.AddMinutes(40);
        dir.WriteSession(restart, LogText.Visit(restart.AddMinutes(1), Loc.Public("333"), "C"));

        using var harness = new EngineHarness(dir.Path, restart.AddMinutes(3), Process(restart));
        harness.Engine.Initialize();

        var rows = Rows(harness);

        Assert.Equal(["111", "333"], rows.Select(r => r.InstanceId));
        Assert.False(rows[1].ExcludedBefore);
    }

    /// <summary>先頭の行には前がないので、対象外から入っていても出さない。</summary>
    [Fact]
    public void 先頭の行には出さない()
    {
        Assert.False(RowFormatter.ExcludedBetween(null, Record(20, excludedBefore: true)));
        Assert.True(RowFormatter.ExcludedBetween(Record(60, leftMinutesAgo: 59), Record(58, excludedBefore: true)));

        var previous = Record(60, leftMinutesAgo: 59);
        previous.ExcludedAfter = true;
        Assert.True(RowFormatter.ExcludedBetween(previous, Record(58)));

        Assert.False(RowFormatter.ExcludedBetween(Record(60, leftMinutesAgo: 59), Record(58)));
    }

    /// <summary>文言は分数を付けず「対象外のインスタンスへ移動」（2026-09-26のユーザー指定。2026-09-27に「ロギング」を外した）。</summary>
    [Fact]
    public void 帯の文字は対象外のインスタンスへ移動()
    {
        Assert.Equal("∧ 対象外のインスタンスへ移動 ∨", string.Concat(RowFormatter.BandSegments(crashed: false, excluded: true).Select(s => s.Text)));
    }

    /// <summary>帯の縦幅はインスタンスの行の1/3。その行の高さに足して数える。</summary>
    [Fact]
    public void 帯の縦幅は行の三分の一で行の高さに足される()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        Assert.Equal((int)MathF.Round(style.RowHeight / 3f), style.AbsenceBandHeight);

        var rows = RowFormatter.Build(
            [Record(90, leftMinutesAgo: 80), Record(5, id: "29719", excludedBefore: true)],
            currentEventId: null,
            Time,
            Now);

        var layouts = renderer.Measure(rows);

        Assert.Equal(style.RowHeight, layouts[0].Height);
        Assert.Equal(style.RowHeight + style.AbsenceBandHeight, layouts[1].Height);
    }

    /// <summary>背景はインスタンスの行よりわずかに明るくする（2026-09-21のユーザー指定）。</summary>
    [Fact]
    public void 帯の背景は行よりわずかに明るい()
    {
        var style = new PanelStyle();

        Assert.True(style.AbsenceBand.R > style.Surface.R);
        Assert.True(style.AbsenceBand.G > style.Surface.G);
        Assert.True(style.AbsenceBand.B > style.Surface.B);

        // 「わずかに」なので、現在地の行の色ほどは離さない。
        var lift = style.AbsenceBand.G - style.Surface.G;
        Assert.InRange(lift, 1, style.CurrentRow.G - style.Surface.G);
    }

    /// <summary>
    /// 実際に描いたとき、2行目の上に帯が入り、その背景は行より明るく、中に文字がある。
    /// 行の中身（IDなど）は帯の下から始まる。
    /// </summary>
    [Fact]
    public void 実際に描くと行の間に帯が入る()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var rows = RowFormatter.Build(
            [Record(90, leftMinutesAgo: 80), Record(5, id: "29719", excludedBefore: true)],
            currentEventId: null,
            Time,
            Now);

        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, 0f);

        var pixels = renderer.GetPixels();

        (byte B, byte G, byte R, byte A) At(int x, int y)
        {
            var i = ((y * style.Width) + x) * 4;
            return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
        }

        var bandTop = style.ViewportTop + style.RowHeight;
        var bandMiddle = bandTop + (style.AbsenceBandHeight / 2);

        // 文字から離れた左端で色を見る。背景は置き換えで塗るので設定した色そのものになる。
        var band = At(8, bandMiddle);
        var surface = At(8, style.ViewportTop + (style.RowHeight / 2));

        Assert.Equal(style.BackgroundAlpha, band.A);
        Assert.True(band.G > surface.G, $"帯（G={band.G}）は行（G={surface.G}）より明るいはず");

        // GDI+ は内部で不透明度を掛けた値を持つので、取り出した色は1程度ずれることがある。
        Assert.InRange(band.G, style.AbsenceBand.G - 2, style.AbsenceBand.G + 2);

        // 帯の中に文字がある（背景より不透明な画素）。
        var hasText = false;
        for (var y = bandTop; y < bandTop + style.AbsenceBandHeight && !hasText; y++)
        {
            for (var x = 0; x < style.Width; x++)
            {
                if (At(x, y).A > style.BackgroundAlpha)
                {
                    hasText = true;
                    break;
                }
            }
        }

        Assert.True(hasText, "帯には「∧ 対象外のインスタンスへ移動 ∨」が出るはず");

        // 帯の上（1行目）と下（2行目の中身）は行の背景のまま。
        Assert.Equal(surface.G, At(8, bandTop - 2).G);
        Assert.Equal(surface.G, At(8, bandTop + style.AbsenceBandHeight + 2).G);
    }

    /// <summary>
    /// 帯は上下を端まで伸ばした線で挟む。ふつうの行の区切りは左右の余白のぶんだけ短いまま
    /// （2026-09-21のユーザー指定）。
    /// </summary>
    [Fact]
    public void 帯の上下の線は端まで伸ばす()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        // 1行目→2行目は帯なし、2行目→3行目は対象外を経由したので帯あり。
        var rows = RowFormatter.Build(
            [Record(90, leftMinutesAgo: 80), Record(75, leftMinutesAgo: 40, id: "29719"), Record(5, id: "39437", excludedBefore: true)],
            currentEventId: null,
            Time,
            Now);

        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, 0f);

        var pixels = renderer.GetPixels();

        // 区切りの線（Rule = #2d3b45）は、行の背景（Surface = #131b22）より明るい。
        var background = pixels[((((style.ViewportTop + 20) * style.Width) + 2) * 4) + 1];
        bool Painted(int x, int y) => pixels[(((y * style.Width) + x) * 4) + 1] > background + 15;

        // 1行目と2行目の区切り（帯に接していない）は、左右の余白のぶんだけ短いまま。
        var plainRule = style.ViewportTop + style.RowHeight - 1;
        Assert.False(Painted(2, plainRule), "ふつうの区切りは左端まで伸ばさない");
        Assert.True(Painted(style.PaddingLeft + 2, plainRule));

        // 帯の上（＝2行目の区切り）と下は端まで。
        var bandTop = style.ViewportTop + (style.RowHeight * 2);

        Assert.True(Painted(0, bandTop - 1), "帯の上の線は左端まで伸びるはず");
        Assert.True(Painted(style.Width - 1, bandTop - 1), "帯の上の線は右端まで伸びるはず");

        var bandBottom = bandTop + style.AbsenceBandHeight - 1;

        Assert.True(Painted(0, bandBottom), "帯の下にも線を出すはず");
        Assert.True(Painted(style.Width - 1, bandBottom), "帯の下の線は右端まで伸びるはず");
    }

    /// <summary>
    /// 現在地の行の上の線も端まで伸ばす（2026-09-21のユーザー指定）。
    /// いま滞在している行を、それまでの履歴から切り離して見せる。
    /// </summary>
    [Fact]
    public void 現在地の行の上の線も端まで伸ばす()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var records = new List<VisitRecord>
        {
            Record(90, leftMinutesAgo: 80),
            Record(75, leftMinutesAgo: 70, id: "29719"),
            Record(60, id: "39437"),
        };

        var rows = RowFormatter.Build(records, records[^1].EventId, Time, Now);

        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, 0f);

        var pixels = renderer.GetPixels();

        var background = pixels[((((style.ViewportTop + 20) * style.Width) + 2) * 4) + 1];
        bool Painted(int x, int y) => pixels[(((y * style.Width) + x) * 4) + 1] > background + 15;

        Assert.True(rows[^1].IsCurrent);
        Assert.False(rows[^1].ExcludedBefore);

        // 2行目と3行目（現在地）の境目。
        var rule = style.ViewportTop + (style.RowHeight * 2) - 1;

        Assert.True(Painted(0, rule), "現在地の行の上の線は左端まで伸びるはず");
        Assert.True(Painted(style.Width - 1, rule), "現在地の行の上の線は右端まで伸びるはず");

        // それより前の区切りは今までどおり短いまま。
        Assert.False(Painted(2, style.ViewportTop + style.RowHeight - 1));
    }
}
