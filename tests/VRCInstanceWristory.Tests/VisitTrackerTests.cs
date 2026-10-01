using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Logging;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Tests;

/// <summary>仕様6節（訪問確定と現在地）と受入項目 T06・T25・L03。</summary>
public class VisitTrackerTests
{
    private static readonly DateTime Base = new(2026, 9, 11, 1, 0, 0);

    private sealed class Fixture
    {
        public Fixture(DateTime? nowLocal = null)
        {
            Time = new LogTimeConverter(EngineHarness.Tokyo);
            Time.TryToUtc(nowLocal ?? Base.AddHours(1), out var now);
            Clock = new ManualClock(now);
            Tracker = new VisitTracker("s0001", 0, Time, Clock);
        }

        public LogTimeConverter Time { get; }

        public ManualClock Clock { get; }

        public VisitTracker Tracker { get; }

        /// <summary>1行ごとに拾った離脱。どの訪問をいつ離れたかの確認に使う。</summary>
        public List<VisitLeave> Leaves { get; } = [];

        /// <summary>訪問ごとの「退出時にいた人数」。離れた時点の在室者の数。</summary>
        public Dictionary<string, int> People { get; } = new(StringComparer.Ordinal);

        private long _offset;

        public VisitRecord? Feed(string logText)
        {
            VisitRecord? last = null;

            foreach (var raw in logText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = LogLineParser.Parse(raw, _offset, 0) with { ByteLength = raw.Length + 1 };
                _offset += raw.Length + 1;

                var record = Tracker.Apply(LogEventParser.Parse(line));

                if (Tracker.LastLeave is { } leave)
                    Leaves.Add(leave);

                if (Tracker.LastPeopleCount is { } people)
                    People[people.EventId] = people.Count;

                last ??= record;
            }

            return last;
        }
    }

    /// <summary>「入室 - 退出」表示のための退出時刻（2026-09-20のユーザー指定）。</summary>
    [Fact]
    public void 退出したら離れた訪問のeventIdと時刻を残す()
    {
        var f = new Fixture(Base.AddHours(2));
        var record = f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "テストワールド"));

        Assert.NotNull(record);
        Assert.Empty(f.Leaves);

        var leftAt = Base.AddMinutes(25);
        f.Feed(LogText.LeftRoom(leftAt));

        var leave = Assert.Single(f.Leaves);
        Assert.Equal(record!.EventId, leave.EventId);
        f.Time.TryToUtc(leftAt, out var expected);
        Assert.Equal(expected, leave.AtUtc);
    }

    /// <summary>
    /// 在室者を常に追いかけ、離れた時点の人数を報告する（2026-09-21のユーザー指定→5.30節）。
    /// </summary>
    [Fact]
    public void 離れた時点の在室者の数を人数にする()
    {
        var f = new Fixture(Base.AddHours(2));
        var record = f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "テストワールド", people: 5));

        f.Feed(LogText.LeftRoomWith(Base.AddMinutes(25), people: 5));

        Assert.Equal(5, f.People[record!.EventId]);
    }

    /// <summary>滞在中の出入りも人数に反映する。退出時に数え直すのではない。</summary>
    [Fact]
    public void 滞在中の出入りが人数に反映される()
    {
        var f = new Fixture(Base.AddHours(2));
        var record = f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "テストワールド", people: 5));

        f.Feed(LogText.PlayerLeft(Base.AddMinutes(5), "player0"));
        f.Feed(LogText.PlayerLeft(Base.AddMinutes(6), "player1"));

        f.Feed(LogText.PlayerJoined(Base.AddMinutes(7), "あとから来た人"));

        // 同じ人の重複した行では増えない。
        f.Feed(LogText.PlayerJoined(Base.AddMinutes(8), "あとから来た人"));

        f.Feed(LogText.LeftRoom(Base.AddMinutes(25)));

        Assert.Equal(4, f.People[record!.EventId]);
    }

    /// <summary>
    /// 在室者を1人も把握できていないときは、0人と断定せずに報告しない
    /// （ログの途中から読み始めた場合など）。
    /// </summary>
    [Fact]
    public void 在室者が分からなければ人数を報告しない()
    {
        var f = new Fixture(Base.AddHours(2));
        var record = f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "テストワールド"));

        f.Feed(LogText.LeftRoom(Base.AddMinutes(25)));

        Assert.Single(f.Leaves);
        Assert.False(f.People.ContainsKey(record!.EventId));
    }

    /// <summary>
    /// クラッシュではログに退出が残らない。プロセスの消滅を伝えると、
    /// その時刻を退出とし、そのときの在室者を人数として報告する（2026-09-21のユーザー指定→5.30節）。
    /// </summary>
    [Fact]
    public void プロセスの終了を伝えると退出と人数を報告する()
    {
        var f = new Fixture(Base.AddHours(2));
        var record = f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "テストワールド", people: 9));

        f.Time.TryToUtc(Base.AddMinutes(30), out var crashedAt);
        var leave = f.Tracker.EndSession(crashedAt);

        Assert.NotNull(leave);
        Assert.Equal(record!.EventId, leave!.Value.EventId);
        Assert.Equal(crashedAt, leave.Value.AtUtc);
        Assert.Equal(9, f.Tracker.LastPeopleCount!.Value.Count);
        Assert.Equal(PresenceState.Ended, f.Tracker.State);
        Assert.Equal([crashedAt], f.Tracker.TargetLeaves);

        // 二度目は何も起きない（同じ退出を二重に記録しない）。
        Assert.Null(f.Tracker.EndSession(crashedAt.AddMinutes(1)));
        Assert.Single(f.Tracker.TargetLeaves);
    }

    /// <summary>正常終了のログを読んだあとは、プロセスの消滅を伝えても退出を作らない。</summary>
    [Fact]
    public void 正常終了のあとの終了通知では退出を作らない()
    {
        var f = new Fixture(Base.AddHours(2));
        f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "テストワールド", people: 4));
        f.Feed(LogText.Quit(Base.AddMinutes(20)));

        var leaves = f.Leaves.Count;

        f.Time.TryToUtc(Base.AddMinutes(21), out var exitedAt);
        Assert.Null(f.Tracker.EndSession(exitedAt));
        Assert.Equal(leaves, f.Leaves.Count);
    }

    /// <summary>
    /// 実ログは Destination set → OnLeftRoom → OnPlayerLeft×人数 の順で、
    /// 離脱の判定は Destination set で起きる。集計は OnLeftRoom で途切れてはいけない。
    /// </summary>
    [Fact]
    public void 移動開始の時点の人数を使う()
    {
        var f = new Fixture(Base.AddHours(2));
        var first = f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "A", people: 4));

        var movedAt = Base.AddMinutes(10);
        f.Feed(LogText.DestinationSet(movedAt, Loc.GroupPublic("29719")));
        f.Feed(LogText.LeftRoomWith(movedAt.AddSeconds(1), people: 4));

        var second = f.Feed(LogText.Visit(movedAt.AddSeconds(2), Loc.GroupPublic("29719"), "B"));

        Assert.Equal(4, f.People[first!.EventId]);

        // 次の部屋の人数は、その部屋を離れるまで分からない。
        Assert.False(f.People.ContainsKey(second!.EventId));

        f.Feed(LogText.PlayerLeft(movedAt.AddSeconds(3), "入室後に帰った人"));
        Assert.False(f.People.ContainsKey(second.EventId));
    }

    /// <summary>別の対象インスタンスへ移った場合も、移動を始めた時点で前の訪問を離れたことにする。</summary>
    [Fact]
    public void 対象から対象への移動でも前の訪問の退出を残す()
    {
        var f = new Fixture(Base.AddHours(2));
        var first = f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "A"));

        var movedAt = Base.AddMinutes(10);
        f.Feed(LogText.Move(movedAt, Loc.GroupPublic("29719"), "B"));

        var leave = Assert.Single(f.Leaves);
        Assert.Equal(first!.EventId, leave.EventId);

        // Move は移動開始（Destination set）が1秒前なので、そこを退出時刻とする。
        f.Time.TryToUtc(movedAt.AddSeconds(-1), out var expected);
        Assert.Equal(expected, leave.AtUtc);
        Assert.Equal(PresenceState.InTarget, f.Tracker.State);
    }

    [Fact]
    public void 対象への入室で訪問を作る()
    {
        var f = new Fixture();
        var record = f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "テストワールド"));

        Assert.NotNull(record);
        Assert.Equal("07254", record!.InstanceId);
        Assert.Equal("テストワールド", record.WorldName);
        Assert.Equal(PresenceState.InTarget, f.Tracker.State);
        Assert.Equal(record.EventId, f.Tracker.CurrentEventId);
    }

    [Fact]
    public void 対象外への入室では訪問を作らず現在地だけ変える()
    {
        var f = new Fixture();
        var record = f.Feed(LogText.Visit(Base, Loc.Friends("38046"), "フレンドのワールド"));

        Assert.Null(record);
        Assert.Equal(PresenceState.InExcluded, f.Tracker.State);
        Assert.Null(f.Tracker.CurrentEventId);
    }

    [Fact]
    public void T06_Joiningだけでは訪問を作らない()
    {
        var f = new Fixture();
        f.Feed(LogText.Joining(Base, Loc.GroupPublic("07254")));

        Assert.Equal(PresenceState.Transitioning, f.Tracker.State);
        Assert.Null(f.Tracker.CurrentEventId);
    }

    [Fact]
    public void T06_候補なしの成功は現在地を維持しない()
    {
        var f = new Fixture();
        f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "最初"));
        Assert.Equal(PresenceState.InTarget, f.Tracker.State);

        var record = f.Feed(LogText.Joined(Base.AddSeconds(30)));

        Assert.Null(record);
        Assert.Equal(PresenceState.Unknown, f.Tracker.State);
    }

    [Fact]
    public void T06_候補が30秒を超えたら訪問を作らない()
    {
        var f = new Fixture();
        f.Feed(LogText.Joining(Base, Loc.GroupPublic("07254")));
        var record = f.Feed(LogText.Joined(Base.AddSeconds(31)));

        Assert.Null(record);
        Assert.Equal(PresenceState.Unknown, f.Tracker.State);
    }

    [Fact]
    public void T06_ちょうど30秒なら訪問を作る()
    {
        var f = new Fixture();
        f.Feed(LogText.Joining(Base, Loc.GroupPublic("07254")));
        var record = f.Feed(LogText.Joined(Base.AddSeconds(30)));

        Assert.NotNull(record);
    }

    [Fact]
    public void T06_途中の切断で候補を破棄する()
    {
        var f = new Fixture();
        f.Feed(LogText.Joining(Base, Loc.GroupPublic("07254")));
        f.Feed(LogText.Disconnected(Base.AddSeconds(1)));
        var record = f.Feed(LogText.Joined(Base.AddSeconds(2)));

        Assert.Null(record);
        Assert.Equal(PresenceState.Unknown, f.Tracker.State);
    }

    [Fact]
    public void L03_移動要求の失敗では現在地を維持する()
    {
        var f = new Fixture();
        f.Feed(LogText.Visit(Base, Loc.Public("c1e88b6419"), "のんびり"));
        var before = f.Tracker.CurrentEventId;

        f.Feed(LogText.CouldNotEnter(Base.AddMinutes(2), "Instance is full"));
        f.Feed(LogText.FailedToJoin(Base.AddMinutes(2), Loc.GroupPublic("28006")));

        Assert.Equal(PresenceState.InTarget, f.Tracker.State);
        Assert.Equal(before, f.Tracker.CurrentEventId);
    }

    [Fact]
    public void 遷移開始後の失敗ではUnknownのままにする()
    {
        var f = new Fixture();
        f.Feed(LogText.Visit(Base, Loc.Public("c1e88b6419"), "のんびり"));
        f.Feed(LogText.DestinationSet(Base.AddSeconds(10), Loc.Public("28006")));
        f.Feed(LogText.CouldNotEnter(Base.AddSeconds(11), "Instance is full"));

        Assert.Equal(PresenceState.Unknown, f.Tracker.State);
    }

    [Fact]
    public void T25_古い世界名候補は流用しない()
    {
        var f = new Fixture();
        f.Feed(LogText.Entering(Base, "古い名前"));
        f.Feed(LogText.Joining(Base.AddSeconds(31), Loc.GroupPublic("07254")));
        var record = f.Feed(LogText.Joined(Base.AddSeconds(31)));

        Assert.NotNull(record);
        Assert.Null(record!.WorldName);
    }

    [Fact]
    public void T25_離脱をまたいだ世界名候補は消える()
    {
        var f = new Fixture();
        f.Feed(LogText.Entering(Base, "前のワールド"));
        f.Feed(LogText.LeftRoom(Base.AddSeconds(1)));
        f.Feed(LogText.Joining(Base.AddSeconds(2), Loc.GroupPublic("07254")));
        var record = f.Feed(LogText.Joined(Base.AddSeconds(2)));

        Assert.NotNull(record);
        Assert.Null(record!.WorldName);
    }

    [Fact]
    public void T25_JoiningOrCreatingRoomは世界名候補にしない()
    {
        var f = new Fixture();
        f.Feed(LogText.JoiningOrCreating(Base, "作成中の名前"));
        f.Feed(LogText.Joining(Base.AddSeconds(1), Loc.GroupPublic("07254")));
        var record = f.Feed(LogText.Joined(Base.AddSeconds(1)));

        Assert.NotNull(record);
        Assert.Null(record!.WorldName);
    }

    [Fact]
    public void T25_負の時刻差では訪問を作らない()
    {
        var f = new Fixture();
        f.Feed(LogText.Joining(Base, Loc.GroupPublic("07254")));
        var record = f.Feed(LogText.Joined(Base.AddSeconds(-1)));

        Assert.Null(record);
        Assert.Equal(PresenceState.Unknown, f.Tracker.State);
    }

    [Fact]
    public void 未来の時刻は採用しない()
    {
        var f = new Fixture(nowLocal: Base);
        var future = Base.AddMinutes(10);

        f.Feed(LogText.Visit(future, Loc.GroupPublic("07254"), "未来"));

        Assert.Equal(PresenceState.Unknown, f.Tracker.State);
    }

    [Fact]
    public void 壊れたlocationはUnknownにして以前の状態を残さない()
    {
        var f = new Fixture();
        f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "最初"));
        f.Feed(LogText.Joining(Base.AddSeconds(10), $"{Loc.WorldA}:07254~unknownTag(x)"));

        Assert.Equal(PresenceState.Unknown, f.Tracker.State);
        Assert.Null(f.Tracker.CurrentLocation);
    }

    [Fact]
    public void 終了は同じセッションの後続ログで解除しない()
    {
        var f = new Fixture();
        f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "最初"));
        f.Feed(LogText.Quit(Base.AddMinutes(1)));
        Assert.Equal(PresenceState.Ended, f.Tracker.State);
        Assert.NotNull(f.Tracker.QuitAtUtc);

        f.Feed(LogText.Visit(Base.AddMinutes(2), Loc.GroupPublic("39437"), "終了後"));

        Assert.Equal(PresenceState.Ended, f.Tracker.State);
    }

    [Fact]
    public void ワールドタブを開くと表示できる状態になる()
    {
        var f = new Fixture();
        Assert.False(f.Tracker.MenuPageOpen); // 分からないうちは閉じている扱い

        f.Feed(LogText.WorldsTabShown(Base));
        Assert.True(f.Tracker.MenuPageOpen);
    }

    [Theory]
    [InlineData("MainMenuWorlds")]
    [InlineData("MainMenuLiveNow")]
    [InlineData("MainMenuSocial")]
    [InlineData("MainMenuVRChatPlusSubscriptions")]
    public void 対象のどのページでも表示できる状態になる(string page)
    {
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base, page));

        Assert.True(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void 対象外のページでは開かない()
    {
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base, "MainMenuSettings"));

        Assert.False(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void ページを隠すログでは閉じない()
    {
        // 2026-09-16のユーザー指定: OnPageAboutToHide / OnPageHidden は実機で
        // 期待どおりに出ないことが多いため、閉じる判定には使わない。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.WorldsTabHidden(Base.AddSeconds(5)));

        Assert.True(f.Tracker.MenuPageOpen);
    }

    [Theory]
    [InlineData("MainMenuWorlds")]
    [InlineData("MainMenuSocial")]
    // 対象外のページへ移ってから閉じても閉じる（B / Y で閉じたときと同じ）。
    [InlineData("MainMenuMarketplace")]
    public void メインメニューを閉じたログで閉じる(string closedFrom)
    {
        // 2026-10-01: B を押し続けたままの Y の短押し（とその逆）で閉じても、パネルが残るとの報告（→実装メモ5.104）。
        // メインメニューを閉じたときにだけ出る並び（OnWillCloseAllChildPages の直後の AboutToHide）で閉じる。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.MainMenuClosed(Base.AddSeconds(4), closedFrom));

        Assert.False(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void タブの切り替えでは閉じない()
    {
        // 実ログ（2026-10-01 12:40:22）の並び。隠れるページの AboutToHide の前に OnWillCloseAllChildPages は来ず、
        // OnWillCloseAllChildPages は開くページのもので、AboutToHide とは隣り合わない。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base, "MainMenuSocial"));

        var t = Base.AddSeconds(6);
        f.Feed(LogText.Line(t, "VP MainMenuSocial OnPageAboutToHide()", "Warning")
               + LogText.Line(t, "VP MainMenuWorlds OnPageAddedToStack()", "Warning")
               + LogText.Line(t, "VP MainMenuWorlds AboutToNavigate() MainMenuWorlds", "Warning")
               + LogText.Line(t, "VP MainMenuWorlds OnWillCloseAllChildPages()", "Warning")
               + LogText.Line(t, "VP MainMenuWorlds Start()", "Warning")
               + LogText.Line(t, "VP MainMenuWorlds OnNavigated() MainMenuWorlds", "Warning")
               + LogText.Line(t, "VP MainMenuWorlds OnPageAboutToShow()", "Warning")
               + LogText.Line(t, "VP MainMenuWorlds OnPageShown()", "Warning")
               + LogText.Line(t, "VP MainMenuSocial OnPageHidden()", "Warning")
               + LogText.Line(t, "VP MainMenuSocial Finish()", "Warning"));

        Assert.True(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void 子のページを閉じただけでは閉じない()
    {
        // 実ログ（2026-09-30 23:49:52）: OnWillCloseAllChildPages のあとに別の VP の行が挟まれば、並びは途切れる。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base, "MainMenuSocial"));

        var t = Base.AddSeconds(4);
        f.Feed(LogText.Line(t, "VP MainMenuSocial OnWillCloseAllChildPages()", "Warning")
               + LogText.Line(t, "VP MainMenuSocial OnPageCovered()", "Warning")
               + LogText.Line(t.AddSeconds(4), "VP MainMenuSocial OnPageUncovered()", "Warning")
               + LogText.Line(t.AddSeconds(5), "VP MainMenuSocial OnPageAboutToHide()", "Warning"));

        Assert.True(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void 別のページの並びでは閉じない()
    {
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));

        var t = Base.AddSeconds(4);
        f.Feed(LogText.Line(t, "VP MainMenuSocial OnWillCloseAllChildPages()", "Warning")
               + LogText.Line(t, "VP MainMenuWorlds OnPageAboutToHide()", "Warning"));

        Assert.True(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void メインメニュー以外のページの並びでは閉じない()
    {
        // ポップアップ（Popups_Root）も同じ並びで閉じるが、メインメニューを閉じたのではない。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.MainMenuClosed(Base.AddSeconds(4), "Popups_Root"));

        Assert.True(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void 閉じたあと開き直せば出る()
    {
        // 実ログ（2026-10-01 12:46:06〜12:46:08）: 閉じてすぐ開き直す。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.MainMenuClosed(Base.AddSeconds(1)));
        f.Feed(LogText.WorldsTabShown(Base.AddSeconds(2)));

        Assert.True(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void ロード画面の間はメインメニューを閉じたログでも閉じない()
    {
        // ロード画面の間は、メニューとは別に出している（→5.31節）。実ログでは閉じる並びは退出より先に出るが、念のため確かめる。
        var f = new Fixture();
        f.Feed(LogText.Visit(Base, Loc.GroupPublic("39437"), "移動前"));
        f.Feed(LogText.LeftRoom(Base.AddMinutes(1)));
        Assert.True(f.Tracker.InLoadingScreen);

        f.Feed(LogText.MainMenuClosed(Base.AddMinutes(1).AddSeconds(1)));

        Assert.True(f.Tracker.MenuPageOpen);
        Assert.True(f.Tracker.InLoadingScreen);
    }

    [Fact]
    public void 移動の直前に閉じても退出からロード画面の間は出す()
    {
        // 実ログ（2026-10-01 12:42:17〜18）: Destination set → 閉じる並び → OnLeftRoom。
        var f = new Fixture();
        f.Feed(LogText.Visit(Base, Loc.GroupPublic("39437"), "移動前"));
        f.Feed(LogText.WorldsTabShown(Base.AddMinutes(1)));

        var t = Base.AddMinutes(1).AddSeconds(6);
        f.Feed(LogText.DestinationSet(t, Loc.GroupPublic("05045")));
        f.Feed(LogText.MainMenuClosed(t.AddSeconds(1)));
        Assert.False(f.Tracker.MenuPageOpen);

        f.Feed(LogText.LeftRoom(t.AddSeconds(1)));

        Assert.True(f.Tracker.MenuPageOpen);
        Assert.True(f.Tracker.InLoadingScreen);
    }

    [Fact]
    public void インスタンスから退出しても閉じない()
    {
        // 2026-09-21のユーザー指定: 退出では閉じず、ロード画面の間も出したままにする。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.LeftRoom(Base.AddSeconds(10)));

        Assert.True(f.Tracker.MenuPageOpen);
        Assert.False(f.Tracker.AwaitingViewAngleClose); // まだ入室していないので角度でも閉じない
    }

    [Fact]
    public void 別インスタンスへ移ると入室後に角度で閉じる状態になる()
    {
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.Move(Base.AddSeconds(10), Loc.GroupPublic("07254"), "移動先"));

        Assert.True(f.Tracker.MenuPageOpen);
        Assert.True(f.Tracker.AwaitingViewAngleClose);
        Assert.Equal(PresenceState.InTarget, f.Tracker.State);

        // VR側が角度で隠した時点で閉じる。以降は待たない。
        f.Tracker.CloseMenuPage();

        Assert.False(f.Tracker.MenuPageOpen);
        Assert.False(f.Tracker.AwaitingViewAngleClose);

        // もう一度メインメニューを開けば出る。移動していないので角度では閉じない。
        f.Feed(LogText.WorldsTabShown(Base.AddSeconds(20)));

        Assert.True(f.Tracker.MenuPageOpen);
        Assert.False(f.Tracker.AwaitingViewAngleClose);
    }

    [Fact]
    public void 移動先が対象外でも入室後に角度で閉じる状態になる()
    {
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.Move(Base.AddSeconds(10), Loc.Friends("99999"), "Friends"));

        Assert.True(f.Tracker.MenuPageOpen);
        Assert.True(f.Tracker.AwaitingViewAngleClose);
        Assert.Equal(PresenceState.InExcluded, f.Tracker.State);
    }

    [Fact]
    public void 閉じていてもロード画面の間は出す()
    {
        // 2026-09-22のユーザー指定: ロード画面の間はメインメニューが出ているのと同じ扱い。
        // 閉じた状態で移動しても、ロード画面では出す。
        var f = new Fixture();
        Assert.False(f.Tracker.MenuPageOpen);

        f.Feed(LogText.LeftRoom(Base.AddSeconds(10)));

        Assert.True(f.Tracker.InLoadingScreen);
        Assert.True(f.Tracker.MenuPageOpen);
        Assert.False(f.Tracker.AwaitingViewAngleClose);

        f.Feed(LogText.Move(Base.AddSeconds(20), Loc.GroupPublic("07254"), "移動先"));

        Assert.False(f.Tracker.InLoadingScreen);
        Assert.True(f.Tracker.MenuPageOpen);
        Assert.True(f.Tracker.AwaitingViewAngleClose);
    }

    [Fact]
    public void ロード画面で出さない設定なら移動の始まりで閉じ出し直さない()
    {
        // 設定 showPanelDuringLoading をオフにしたとき（→実装メモ5.62）。
        // メニューから移動すると Destination set の時点でロード画面が始まるので、そこで閉じる。
        var f = new Fixture();
        f.Tracker.ShowDuringLoadingScreen = false;
        f.Feed(LogText.WorldsTabShown(Base));
        Assert.True(f.Tracker.MenuPageOpen);

        f.Feed(LogText.DestinationSet(Base.AddSeconds(5), Loc.GroupPublic("07254")));
        Assert.False(f.Tracker.MenuPageOpen);

        f.Feed(LogText.LeftRoom(Base.AddSeconds(10)));
        Assert.True(f.Tracker.InLoadingScreen);
        Assert.False(f.Tracker.MenuPageOpen);

        f.Feed(LogText.Move(Base.AddSeconds(20), Loc.GroupPublic("07254"), "移動先"));
        Assert.False(f.Tracker.InLoadingScreen);
        Assert.False(f.Tracker.MenuPageOpen);
        Assert.False(f.Tracker.AwaitingViewAngleClose);

        // メインメニューを開けば、いつもどおり出る。
        f.Feed(LogText.WorldsTabShown(Base.AddSeconds(30)));
        Assert.True(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void ロード画面の途中で設定を変えるとその場で出す隠す()
    {
        var f = new Fixture();
        f.Feed(LogText.LeftRoom(Base.AddSeconds(10)));
        Assert.True(f.Tracker.MenuPageOpen);

        f.Tracker.ShowDuringLoadingScreen = false;
        Assert.False(f.Tracker.MenuPageOpen);

        f.Tracker.ShowDuringLoadingScreen = true;
        Assert.True(f.Tracker.MenuPageOpen);

        // ロード画面の外では、切り替えても表示は変えない。
        f.Feed(LogText.Move(Base.AddSeconds(20), Loc.GroupPublic("07254"), "移動先"));
        f.Tracker.CloseMenuPage();
        f.Tracker.ShowDuringLoadingScreen = false;
        f.Tracker.ShowDuringLoadingScreen = true;
        Assert.False(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void 角度で閉じる状態になるのはロード完了の行()
    {
        // 待ちへ移るのは Successfully joined room ではなく Finished entering world.
        // （2026-09-22のユーザー指定）。入室の確定から数秒あとに出る行。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.DestinationSet(Base.AddSeconds(9), Loc.GroupPublic("07254")));
        f.Feed(LogText.LeftRoom(Base.AddSeconds(9)));
        f.Feed(LogText.Entering(Base.AddSeconds(10), "移動先"));
        f.Feed(LogText.Joining(Base.AddSeconds(10), Loc.GroupPublic("07254")));
        f.Feed(LogText.Joined(Base.AddSeconds(10)));

        // 入室は確定したが、まだロード画面の中。
        Assert.Equal(PresenceState.InTarget, f.Tracker.State);
        Assert.True(f.Tracker.InLoadingScreen);
        Assert.False(f.Tracker.AwaitingViewAngleClose);

        f.Feed(LogText.FinishedEntering(Base.AddSeconds(18)));

        Assert.False(f.Tracker.InLoadingScreen);
        Assert.True(f.Tracker.AwaitingViewAngleClose);
    }

    [Fact]
    public void ロード画面の外のロード完了行では待ちにならない()
    {
        // 退出を見ていない（ロード画面に入っていない）ときは、何も起こさない。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.FinishedEntering(Base.AddSeconds(10)));

        Assert.True(f.Tracker.MenuPageOpen);
        Assert.False(f.Tracker.AwaitingViewAngleClose);
    }

    [Fact]
    public void ロード中にB_Yで閉じたら入室しても角度の待ちは残らない()
    {
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.LeftRoom(Base.AddSeconds(10)));

        f.Tracker.CloseMenuPage();

        f.Feed(LogText.Visit(Base.AddSeconds(20), Loc.GroupPublic("07254"), "移動先"));

        Assert.False(f.Tracker.MenuPageOpen);
        Assert.False(f.Tracker.AwaitingViewAngleClose);
    }

    [Fact]
    public void 移動後にメインメニューを開いても角度の待ちは消えない()
    {
        // 2026-09-21のユーザー指定: 入室後の初回は、メインメニューが開いているかどうかに
        // 関係なく角度で閉じる。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.Move(Base.AddSeconds(10), Loc.GroupPublic("07254"), "移動先"));
        f.Feed(LogText.WorldsTabShown(Base.AddSeconds(20)));

        Assert.True(f.Tracker.MenuPageOpen);
        Assert.True(f.Tracker.AwaitingViewAngleClose);
    }

    [Fact]
    public void 切断だけでは閉じない()
    {
        // 退出は OnLeftRoom で判定する。切断は現在地だけを未確定に戻す。
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.Disconnected(Base.AddSeconds(10)));

        Assert.True(f.Tracker.MenuPageOpen);
        Assert.Equal(PresenceState.Unknown, f.Tracker.State);
    }

    [Fact]
    public void VR側の操作で閉じられる()
    {
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));

        f.Tracker.CloseMenuPage();

        Assert.False(f.Tracker.MenuPageOpen);
    }

    [Fact]
    public void クライアント終了でワールドタブも閉じる()
    {
        var f = new Fixture();
        f.Feed(LogText.WorldsTabShown(Base));
        f.Feed(LogText.LeftRoom(Base.AddSeconds(4)));
        f.Feed(LogText.Quit(Base.AddSeconds(5)));

        Assert.False(f.Tracker.MenuPageOpen);
        Assert.False(f.Tracker.AwaitingViewAngleClose);
    }

    [Fact]
    public void 同じlocationへの別の成功は別の訪問になる()
    {
        var f = new Fixture();
        var first = f.Feed(LogText.Visit(Base, Loc.GroupPublic("07254"), "1回目"));
        var second = f.Feed(LogText.Move(Base.AddMinutes(1), Loc.GroupPublic("07254"), "2回目"));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.EventId, second!.EventId);
        Assert.Equal(first.LocationKey, second.LocationKey);
    }
}
