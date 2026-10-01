using System.Text;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 合成ログでの受入項目（T09〜T12・T15・T17・T24・T27・T29）。
/// ログの追従・復元・保存をSteamVRなしで確認する。
/// </summary>
public class EngineBehaviourTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    /// <summary>
    /// 3時間前に終了した起動を記録しておく。これで対象セッションの直前の間隔が60分を超え、
    /// 0開始の基準を確定できる（記録がないと仕様どおり「回数不明」になる）。
    /// </summary>
    /// <summary>ファイルの更新日時をログ上の時刻に合わせる。読み込み範囲の判定を実際の経過に近づける。</summary>
    private static void Touch(string file, DateTime local)
    {
        var time = new LogTimeConverter(EngineHarness.Tokyo);
        time.TryToUtc(local, out var utc);
        File.SetLastWriteTimeUtc(file, utc);
    }

    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    [Fact]
    public void T08_対象に滞在している間は65分経っても消さない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.True(harness.Snapshot().ContentReady);

        // 入室から65分。対象に滞在している間は期限を数え始めない（2026-09-18のユーザー指定）。
        harness.SetNow(SessionStart.AddMinutes(66));
        harness.Engine.Update();

        var snapshot = harness.Snapshot();

        Assert.Single(snapshot.History);
        Assert.True(snapshot.ContentReady);
        Assert.Equal(PresenceState.InTarget, snapshot.Presence);
        Assert.Null(snapshot.RetentionDeadlineUtc); // 期限なし
        Assert.Equal(1, harness.Engine.Counter.Counts.Values.Single());
    }

    /// <summary>
    /// 「入室 - 退出」表示のもと（2026-09-20のユーザー指定）。
    /// 履歴の各行へ、その訪問を離れた時刻が入る。滞在中の行は入らない。
    /// </summary>
    [Fact]
    public void 履歴の行に退出時刻が入る()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(10), Loc.GroupPublic("222"), "B")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(11)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(12), Process(SessionStart));
        harness.Engine.Initialize();

        var history = harness.Snapshot().History;
        Assert.Equal(["111", "222"], history.Select(v => v.InstanceId));

        // 1件目は移動を始めた時点（Move の Destination set は1秒前）に離れている。
        Assert.Equal(harness.Utc(SessionStart.AddMinutes(10).AddSeconds(-1)), history[0].LeftAtUtc);

        // 2件目はまだ滞在中。
        Assert.Null(history[1].LeftAtUtc);

        // 退出すると、そのときの時刻が入る。
        var leftAt = SessionStart.AddMinutes(20);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));
        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();

        Assert.Equal(harness.Utc(leftAt), harness.Snapshot().History[1].LeftAtUtc);
    }

    /// <summary>
    /// 退出時にそのインスタンスにいた人数が履歴の行に入る（2026-09-20のユーザー指定）。
    /// 滞在中ずっと追いかけている在室者の数から取る。滞在中の行は分からないので入らない。
    /// </summary>
    [Fact]
    public void 履歴の行に退出時の人数が入る()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        var movedAt = SessionStart.AddMinutes(10);

        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A", people: 7)

            // 実ログの並び: Destination set → OnLeftRoom → OnPlayerLeft×人数 → 次の入室。
            + LogText.DestinationSet(movedAt.AddSeconds(-1), Loc.GroupPublic("222"))
            + LogText.LeftRoomWith(movedAt.AddSeconds(-1), people: 7)
            + LogText.Visit(movedAt, Loc.GroupPublic("222"), "B", people: 2)
            + LogText.WorldsTabShown(movedAt.AddMinutes(1)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(12), Process(SessionStart));
        harness.Engine.Initialize();

        var history = harness.Snapshot().History;
        Assert.Equal(["111", "222"], history.Select(v => v.InstanceId));
        Assert.Equal(7, history[0].PeopleCount);
        Assert.Null(history[1].PeopleCount);

        // 退出すると、そのときの人数が入る。
        var leftAt = SessionStart.AddMinutes(20);
        TempLogDirectory.Append(file, LogText.LeftRoomWith(leftAt, people: 2));
        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();

        Assert.Equal(2, harness.Snapshot().History[1].PeopleCount);
    }

    [Fact]
    public void 退出から60分でどれだけ古い行もまとめて消える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        // 1時間半にわたって対象を渡り歩き、そのあと退出する。
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(90), Loc.GroupPublic("222"), "B")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(91)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(92), Process(SessionStart));
        harness.Engine.Initialize();

        // 91分前の行も、対象を離れていなければ残る。
        Assert.Equal(["111", "222"], harness.Snapshot().History.Select(v => v.InstanceId));

        var leftAt = SessionStart.AddMinutes(95);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));

        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();

        var counting = harness.Snapshot();
        Assert.Equal(2, counting.History.Count);
        Assert.Equal(harness.Utc(leftAt.AddMinutes(60)), counting.RetentionDeadlineUtc);

        // 退出の59分59秒後。まだ1件も消えていない。
        harness.SetNow(leftAt.AddSeconds(59 * 60 + 59));
        harness.Engine.Update();
        Assert.Equal(2, harness.Snapshot().History.Count);

        // ちょうど60分でまとめて消える。
        harness.SetNow(leftAt.AddMinutes(60));
        harness.Engine.Update();

        var expired = harness.Snapshot();
        Assert.Empty(expired.History);
        Assert.False(expired.ContentReady);

        // 自動リセットが入ったので、回数は数え直す（2026-10-01のユーザー指定→実装メモ5.110）。行に付けた回数は書き換えない。
        Assert.Empty(harness.Engine.Counter.Counts);
        Assert.Equal([1, 1], harness.Visits.Select(v => v.VisitOrdinal));
    }

    /// <summary>
    /// 見出しの「カウントリセット」（2026-09-19のユーザー指定）。
    /// 押した時点をもう一度の退出として扱い、そこから60分を数え直す。
    /// </summary>
    [Fact]
    public void カウントリセットを押すと押した時点から60分を数え直す()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        var leftAt = SessionStart.AddMinutes(3);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));

        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();
        Assert.Equal(harness.Utc(leftAt.AddMinutes(60)), harness.Snapshot().RetentionDeadlineUtc);

        // 残り10分のところで押す。期限は押した時点+60分になり、残り時間も60:00へ戻る。
        var pressedAt = leftAt.AddMinutes(50);
        harness.SetNow(pressedAt);
        harness.Engine.Update();
        harness.Engine.ResetRetention();

        var reset = harness.Snapshot();
        Assert.Equal(harness.Utc(pressedAt.AddMinutes(60)), reset.RetentionDeadlineUtc);
        Assert.Equal(HistoryStore.DefaultRetention, reset.RetentionRemaining(harness.Clock.UtcNow));

        // 元の期限（退出+60分）では消えない。
        harness.SetNow(leftAt.AddMinutes(61));
        harness.Engine.Update();
        Assert.Single(harness.Snapshot().History);

        // 押した時点から59分59秒でも残る。
        harness.SetNow(pressedAt.AddSeconds(59 * 60 + 59));
        harness.Engine.Update();
        Assert.Single(harness.Snapshot().History);

        // 押した時点から60分でまとめて消える。
        harness.SetNow(pressedAt.AddMinutes(60));
        harness.Engine.Update();

        var expired = harness.Snapshot();
        Assert.Empty(expired.History);
        Assert.Empty(harness.Engine.Counter.Counts); // 自動リセットで数え直す（→実装メモ5.110）
    }

    /// <summary>
    /// 2回目以降の「カウント延長」で履歴が消えていた不具合（2026-09-25のユーザー報告→実装メモ5.36）。
    ///
    /// 押した時刻を最後の1回しか覚えていなかったため、元の期限（退出+60分）を過ぎてから押し直すと、
    /// 前の押下が取り消していた退出の期限が成立してしまい、押した瞬間に全行が消えていた。
    /// 何度押しても、最後に押した時点から60分は消えないことを確かめる。
    /// </summary>
    [Fact]
    public void 元の期限を過ぎてから延長を押し直しても消えない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        var leftAt = SessionStart.AddMinutes(3);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));
        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();

        // 退出から30分ごとに4回押す。2回目以降はどれも、1つ前までの期限を過ぎてから押している
        // （2回目は退出+60分、3回目は1回目+60分を過ぎたあと）。
        var pressedAt = leftAt;

        for (var press = 1; press <= 4; press++)
        {
            pressedAt = leftAt.AddMinutes(50 * press - 20);

            // 押す直前まで残っている（前の押下の期限の内側）。
            harness.SetNow(pressedAt);
            harness.Engine.Update();
            Assert.Single(harness.Snapshot().History);

            harness.Engine.ResetRetention();

            var pressed = harness.Snapshot();
            Assert.Single(pressed.History);
            Assert.Equal(harness.Utc(pressedAt.AddMinutes(60)), pressed.RetentionDeadlineUtc);
        }

        // 最後に押した時点から59分59秒でも残り、60分でまとめて消える。
        harness.SetNow(pressedAt.AddSeconds(59 * 60 + 59));
        harness.Engine.Update();
        Assert.Single(harness.Snapshot().History);

        harness.SetNow(pressedAt.AddMinutes(60));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);
    }

    /// <summary>
    /// 延長で退出+60分より後まで残した行は、対象へ戻ってから押しても消えない（2026-09-25→実装メモ5.36）。
    /// 対象に滞在している間の押下は何も変えない決まり（→5.17）だが、以前はこの押下が
    /// 前の押下の記録を上書きしていたため、戻る前の行がまとめて消えていた。
    /// </summary>
    [Fact]
    public void 延長で残した行は対象へ戻ってから押しても消えない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        var leftAt = SessionStart.AddMinutes(3);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));
        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();

        // 退出から40分で延長し、退出+70分（元の期限の後）に対象へ戻る。
        harness.SetNow(leftAt.AddMinutes(40));
        harness.Engine.Update();
        harness.Engine.ResetRetention();

        TempLogDirectory.Append(file, LogText.Visit(leftAt.AddMinutes(70), Loc.GroupPublic("222"), "B"));
        harness.SetNow(leftAt.AddMinutes(71));
        harness.Engine.Update();
        Assert.Equal(["111", "222"], harness.Snapshot().History.Select(v => v.InstanceId));

        // 滞在中に押しても期限は付かず、戻る前の行も消えない。
        harness.Engine.ResetRetention();

        var staying = harness.Snapshot();
        Assert.Equal(["111", "222"], staying.History.Select(v => v.InstanceId));
        Assert.Null(staying.RetentionDeadlineUtc);

        // 滞在が続けば、どれだけ経っても消えない。
        harness.SetNow(leftAt.AddMinutes(200));
        harness.Engine.Update();
        Assert.Equal(["111", "222"], harness.Snapshot().History.Select(v => v.InstanceId));
    }

    /// <summary>
    /// 見出しの「リセット」（確認を挟む・2026-09-27のユーザー指定→実装メモ5.65）。
    /// 対象を離れている間に押すと、期限を待たずに行がすべて消え、あとから来た訪問は新しい行として出る。
    /// </summary>
    [Fact]
    public void 履歴のリセットで行が今すぐすべて消える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30))
            + LogText.LeftRoom(SessionStart.AddMinutes(3))
            + LogText.Visit(SessionStart.AddMinutes(4), Loc.GroupPublic("222"), "B")
            + LogText.LeftRoom(SessionStart.AddMinutes(6)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(7), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();
        Assert.Equal(["111", "222"], harness.Snapshot().History.Select(v => v.InstanceId));

        harness.Engine.ClearHistory();
        Assert.Empty(harness.Snapshot().History);

        // 回数は減らさない（次に同じインスタンスへ入れば続きから数える）。
        Assert.Equal(2, harness.Engine.Counter.Counts.Values.Sum());

        // 消えた行は、あとで Update しても戻らない。あとから来た訪問は出る。
        TempLogDirectory.Append(file, LogText.Visit(SessionStart.AddMinutes(8), Loc.GroupPublic("333"), "C"));
        harness.SetNow(SessionStart.AddMinutes(9));
        harness.Engine.Update();
        Assert.Equal(["333"], harness.Snapshot().History.Select(v => v.InstanceId));
    }

    /// <summary>対象に滞在している間の「リセット」は、いまの滞在の行だけを残す（→実装メモ5.65）。</summary>
    [Fact]
    public void 滞在中の履歴のリセットはいまの行だけを残す()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30))
            + LogText.LeftRoom(SessionStart.AddMinutes(3))
            + LogText.Visit(SessionStart.AddMinutes(4), Loc.GroupPublic("222"), "B"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(5), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();

        harness.Engine.ClearHistory();
        Assert.Equal(["222"], harness.Snapshot().History.Select(v => v.InstanceId));

        // 退出しても、いまの行は退出の時刻が付いてそのまま残る。
        TempLogDirectory.Append(file, LogText.LeftRoom(SessionStart.AddMinutes(10)));
        harness.SetNow(SessionStart.AddMinutes(11));
        harness.Engine.Update();

        var left = Assert.Single(harness.Snapshot().History);
        Assert.Equal("222", left.InstanceId);
        Assert.NotNull(left.LeftAtUtc);
    }

    [Fact]
    public void カウントリセットのあとに対象を離れ直したらその退出から数える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        // 対象に滞在している間は期限がないので、押しても期限は付かない。
        harness.Engine.ResetRetention();
        Assert.Null(harness.Snapshot().RetentionDeadlineUtc);

        // 60分を過ぎても、滞在している限り消えない。
        harness.SetNow(SessionStart.AddMinutes(63));
        harness.Engine.Update();
        Assert.Single(harness.Snapshot().History);

        // 離れたら、そのときの退出から60分を数える（押した時点からではない）。
        var leftAt = SessionStart.AddMinutes(64);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));

        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();
        Assert.Equal(harness.Utc(leftAt.AddMinutes(60)), harness.Snapshot().RetentionDeadlineUtc);

        harness.SetNow(leftAt.AddMinutes(60));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);
    }

    [Fact]
    public void 期限の60分になる前に対象へ戻れば数え直して消さない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.LeftRoom(SessionStart.AddMinutes(2)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(3), Process(SessionStart));
        harness.Engine.Initialize();

        // 退出から59分で対象へ戻る。最初の退出の期限（62分）は成立しない。
        TempLogDirectory.Append(file, LogText.Visit(SessionStart.AddMinutes(61), Loc.GroupPublic("222"), "B"));

        harness.SetNow(SessionStart.AddMinutes(63));
        harness.Engine.Update();

        var snapshot = harness.Snapshot();

        Assert.Equal(["111", "222"], snapshot.History.Select(v => v.InstanceId));
        Assert.Null(snapshot.RetentionDeadlineUtc); // 滞在中なので期限なし

        // 次の退出から数え直す。
        var leftAt = SessionStart.AddMinutes(64);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));
        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();

        Assert.Equal(harness.Utc(leftAt.AddMinutes(60)), harness.Snapshot().RetentionDeadlineUtc);

        harness.SetNow(leftAt.AddMinutes(60));
        harness.Engine.Update();

        Assert.Empty(harness.Snapshot().History);
    }

    [Fact]
    public void 終了ログなしで終了したら終了時刻から60分を数える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        // 対象に滞在したままプロセスが消える。終了時刻を退出として扱う。
        var exitAt = SessionStart.AddMinutes(5);
        harness.SetNow(exitAt);
        harness.Processes.Exit(harness.Utc(exitAt));
        harness.Engine.Update();

        Assert.Single(harness.Snapshot().History);

        harness.SetNow(exitAt.AddSeconds(59 * 60 + 59));
        harness.Engine.Update();
        Assert.Single(harness.Snapshot().History);

        harness.SetNow(exitAt.AddMinutes(60));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);
    }

    /// <summary>
    /// 再起動をまたぐ保持。前の起動での退出から60分以内に対象へ戻っていれば、
    /// 60分より前に書かれたログもさかのぼって読み、古い行を残す。
    /// </summary>
    [Fact]
    public void 退出から60分以内に再起動して対象へ戻れば前の起動の行も残す()
    {
        using var dir = new TempLogDirectory();
        var earlier = SessionStart.AddHours(-3);
        var earlierFile = dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
        Touch(earlierFile, earlier.AddMinutes(30));

        // 1回目: 入室して5分後に終了する。
        var first = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Quit(SessionStart.AddMinutes(5)));

        // 直近60分に書かれたファイルとしては選ばれないようにする（さかのぼって読む経路の確認）。
        Touch(first, SessionStart.AddMinutes(5));

        // 2回目: 30分後に起動し、入室して35分に終了する。
        var second = SessionStart.AddMinutes(30);
        var secondFile = dir.WriteSession(
            second,
            LogText.Visit(second.AddMinutes(1), Loc.GroupPublic("222"), "B")
            + LogText.Quit(second.AddMinutes(5)));

        Touch(secondFile, second.AddMinutes(5));

        // 3回目: さらに25分後に起動して入室し、そのまま滞在し続ける。
        var third = SessionStart.AddMinutes(60);
        dir.WriteSession(third, LogText.Visit(third.AddMinutes(1), Loc.GroupPublic("333"), "C"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(180), Process(third, pid: 777));
        harness.Engine.Initialize();

        var snapshot = harness.Snapshot();

        // どの退出からも60分以内に対象へ戻っているので、179分前の行まで消えていない。
        Assert.Equal(["111", "222", "333"], snapshot.History.Select(v => v.InstanceId));
        Assert.Null(snapshot.RetentionDeadlineUtc);
    }

    /// <summary>
    /// チェックポイントがあって過去の終了時刻が分かっている場合でも、保持が続いている限り
    /// 60分より前のログをさかのぼって読み、古い行を復元する。
    /// </summary>
    [Fact]
    public void アプリ再起動後も保持が続く間は古いセッションの行を読み直す()
    {
        using var dir = new TempLogDirectory();
        var earlier = SessionStart.AddHours(-3);
        var earlierFile = dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
        Touch(earlierFile, earlier.AddMinutes(30));

        var first = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Quit(SessionStart.AddMinutes(5)));

        var restart = SessionStart.AddMinutes(30);
        dir.WriteSession(restart, LogText.Visit(restart.AddMinutes(1), Loc.GroupPublic("222"), "B"));

        var checkpointPath = Path.Combine(dir.Path, "counter-state.json");

        List<int?> ordinals;
        using (var harness = new EngineHarness(dir.Path, restart.AddMinutes(5), Process(restart, pid: 777), new CheckpointStore(checkpointPath)))
        {
            harness.Engine.Initialize();
            ordinals = harness.Snapshot().History.Select(v => v.VisitOrdinal).ToList();
            Assert.Equal(2, ordinals.Count);
        }

        // 1回目のログは60分より前にしか書かれていない状態にする。
        Touch(first, SessionStart.AddMinutes(5));

        // アプリだけを再起動する。終了時刻はチェックポイントから分かるので読み直す理由がない状態。
        using var restarted = new EngineHarness(
            dir.Path,
            SessionStart.AddMinutes(120),
            Process(restart, pid: 777),
            new CheckpointStore(checkpointPath));

        restarted.Engine.Initialize();

        var snapshot = restarted.Snapshot();

        Assert.Equal(["111", "222"], snapshot.History.Select(v => v.InstanceId));
        Assert.Equal(ordinals, snapshot.History.Select(v => v.VisitOrdinal));
    }

    [Fact]
    public void 退出から60分を超えて再起動したら前の起動の行は残さない()
    {
        using var dir = new TempLogDirectory();
        var earlier = SessionStart.AddHours(-3);
        var earlierFile = dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
        Touch(earlierFile, earlier.AddMinutes(30));

        var first = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Quit(SessionStart.AddMinutes(5)));

        Touch(first, SessionStart.AddMinutes(5));

        // 退出から90分後の起動。途中の60分で1回目の行は消えている。
        var restart = SessionStart.AddMinutes(95);
        dir.WriteSession(restart, LogText.Visit(restart.AddMinutes(1), Loc.GroupPublic("222"), "B"));

        using var harness = new EngineHarness(dir.Path, restart.AddMinutes(2), Process(restart, pid: 777));
        harness.Engine.Initialize();

        Assert.Equal(["222"], harness.Snapshot().History.Select(v => v.InstanceId));
    }

    [Fact]
    public void ワールドタブを開くと表示しB_Yボタンで閉じる()
    {
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        // タブの状態が分からないうちは表示しない。
        Assert.False(harness.Snapshot().MenuPageOpen);
        Assert.False(harness.Snapshot().ContentReady);

        TempLogDirectory.Append(file, LogText.WorldsTabShown(SessionStart.AddMinutes(2)));
        harness.SetNow(SessionStart.AddMinutes(3));
        harness.Engine.Update();
        Assert.True(harness.Snapshot().ContentReady);

        // ページを隠すログでは閉じない（実機で当てにならないため）。
        TempLogDirectory.Append(file, LogText.WorldsTabHidden(SessionStart.AddMinutes(4)));
        harness.SetNow(SessionStart.AddMinutes(5));
        harness.Engine.Update();
        Assert.True(harness.Snapshot().ContentReady);

        // B / Y ボタンで閉じる。
        harness.Engine.ClosePanel();
        Assert.False(harness.Snapshot().ContentReady);

        // 履歴が空ならタブを開いても出さない（退出から60分が過ぎた状態）。
        TempLogDirectory.Append(file, LogText.LeftRoom(SessionStart.AddMinutes(6)));
        TempLogDirectory.Append(file, LogText.WorldsTabShown(SessionStart.AddMinutes(66)));
        harness.SetNow(SessionStart.AddMinutes(67));
        harness.Engine.Update();

        var expired = harness.Snapshot();
        Assert.True(expired.MenuPageOpen);
        Assert.Empty(expired.History);
        Assert.False(expired.ContentReady);
    }

    [Fact]
    public void インスタンスの移動中も表示を続け移動後の角度で閉じる()
    {
        // 2026-09-21のユーザー指定。退出では閉じず、ロード画面の間も出したままにする。
        // 次の入室が済んでから、初めて手首の角度で隠れた時点で閉じる。
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(2)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(3), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.True(harness.Snapshot().ContentReady);

        // 退出（ロード画面の始まり）。まだ出したままにする。
        TempLogDirectory.Append(file, LogText.LeftRoom(SessionStart.AddMinutes(4)));
        harness.SetNow(SessionStart.AddMinutes(5));
        harness.Engine.Update();

        var leaving = harness.Snapshot();
        Assert.True(leaving.ContentReady);
        Assert.False(leaving.AwaitingViewAngleClose);

        // ロード中に角度で隠れても閉じない（次の入室まで待つ）。
        harness.Engine.CloseByViewAngle();
        Assert.True(harness.Snapshot().ContentReady);

        // 移動先への入室が済んだら、次に角度で隠れた時点で閉じる。
        TempLogDirectory.Append(file, LogText.Visit(SessionStart.AddMinutes(6), Loc.GroupPublic("222"), "B"));
        harness.SetNow(SessionStart.AddMinutes(7));
        harness.Engine.Update();

        var joined = harness.Snapshot();
        Assert.True(joined.ContentReady);
        Assert.True(joined.AwaitingViewAngleClose);
        Assert.Equal(2, joined.History.Count);

        harness.Engine.CloseByViewAngle();

        var closed = harness.Snapshot();
        Assert.False(closed.MenuPageOpen);
        Assert.False(closed.ContentReady);
        Assert.False(closed.AwaitingViewAngleClose);
        Assert.Equal(2, closed.History.Count); // 履歴は残る

        // それ以降は、もう一度メインメニューを開いたときに出す。
        TempLogDirectory.Append(file, LogText.WorldsTabShown(SessionStart.AddMinutes(8)));
        harness.SetNow(SessionStart.AddMinutes(9));
        harness.Engine.Update();

        var reopened = harness.Snapshot();
        Assert.True(reopened.ContentReady);
        Assert.False(reopened.AwaitingViewAngleClose); // 移動していないので角度では閉じない

        harness.Engine.CloseByViewAngle();
        Assert.True(harness.Snapshot().ContentReady);
    }

    [Fact]
    public void T16_消去のあと同じlocationへ再訪すると回数が続く()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        // 入室してすぐ退出する。ここから60分で最初の行は消える。
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.LeftRoom(SessionStart.AddMinutes(2)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(3), Process(SessionStart));
        harness.Engine.Initialize();

        var revisit = SessionStart.AddMinutes(70);
        TempLogDirectory.Append(file, LogText.Visit(revisit, Loc.GroupPublic("111"), "A"));

        harness.SetNow(revisit.AddMinutes(1));
        harness.Engine.Update();

        var snapshot = harness.Snapshot();

        Assert.Single(snapshot.History); // 最初の訪問は消去済み
        Assert.Equal(2, snapshot.History[0].VisitOrdinal); // 1へ戻さない
    }

    [Fact]
    public void 初めての起動ではこの起動から数える()
    {
        using var dir = new TempLogDirectory();

        // 前の記録を置かない（アプリの初回起動）。前回の終わりから60分以内かは見ないので、回数不明にしない（→実装メモ5.110）。
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        var snapshot = harness.Snapshot();

        Assert.Single(snapshot.History);
        Assert.Equal(1, snapshot.History[0].VisitOrdinal);
        Assert.True(snapshot.BaselineKnown);
    }

    [Fact]
    public void T09_対象と対象外が同じバッチにあってもバッチ後の状態だけになる()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(2), Loc.Friends("999"), "F")
            + LogText.Move(SessionStart.AddMinutes(3), Loc.GroupPublic("222"), "B")
            // 起動時はメニューの状態が分からず閉じている扱いなので、最後に開いた状態にする。
            + LogText.WorldsTabShown(SessionStart.AddMinutes(3).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(4), Process(SessionStart));
        harness.Engine.Initialize();

        var snapshot = harness.Snapshot();

        Assert.Equal(["111", "222"], snapshot.History.Select(v => v.InstanceId));
        Assert.Equal(PresenceState.InTarget, snapshot.Presence);
        Assert.True(snapshot.ContentReady);
    }

    [Fact]
    public void 対象外のインスタンスでも記録はせずワールドタブの状態で表示を決める()
    {
        // 2026-09-13のユーザー指定: 表示条件は「滞在しているインスタンスの種類」ではなく
        // 「メインメニューのワールドタブを開いているか」。記録の対象は従来どおり3種別だけ。
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(2), Loc.Friends("999"), "F"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(3), Process(SessionStart));
        harness.Engine.Initialize();

        var moved = harness.Snapshot();
        Assert.Single(moved.History);                        // Friends は記録しない（Group+ は 2026-09-28 から既定で記録する→5.86）
        Assert.Equal(PresenceState.InExcluded, moved.Presence);

        // 移動のロード画面はメインメニューと同じ扱いなので、ここでは出ている（→5.31）。
        // 角度で隠れた時点で閉じる。
        Assert.True(moved.ContentReady);
        harness.Engine.CloseByViewAngle();

        var closed = harness.Snapshot();
        Assert.False(closed.MenuPageOpen);
        Assert.False(closed.ContentReady);                   // タブが閉じているので非表示

        TempLogDirectory.Append(file, LogText.WorldsTabShown(SessionStart.AddMinutes(3)));
        harness.SetNow(SessionStart.AddMinutes(4));
        harness.Engine.Update();

        var opened = harness.Snapshot();
        Assert.True(opened.MenuPageOpen);
        Assert.True(opened.ContentReady);                    // タブを開けば対象外にいても出す
        Assert.Null(opened.CurrentEventId);                  // 現在地が対象外なので印は付けない

        // B / Y ボタン相当の操作で閉じる。
        harness.Engine.ClosePanel();
        harness.SetNow(SessionStart.AddMinutes(6));
        harness.Engine.Update();

        Assert.False(harness.Snapshot().ContentReady);
    }

    [Fact]
    public void T10_行の途中までの追記は完成してから一度だけ適用する()
    {
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.Single(harness.Visits);

        // 成功行の途中まで追記する（末尾のロード完了行より手前で切る）。
        var complete = LogText.Move(SessionStart.AddMinutes(3), Loc.GroupPublic("222"), "B");
        var cut = complete.Length - LogText.FinishedEntering(SessionStart.AddMinutes(3)).Length - 10;
        TempLogDirectory.Append(file, complete[..cut]);

        harness.SetNow(SessionStart.AddMinutes(3));
        harness.Engine.Update();
        Assert.Single(harness.Visits);

        // 残りが届いたら1回だけ適用する。
        TempLogDirectory.Append(file, complete[cut..]);
        harness.SetNow(SessionStart.AddMinutes(4));
        harness.Engine.Update();

        Assert.Equal(2, harness.Visits.Count);
        Assert.Equal(["111", "222"], harness.Snapshot().History.Select(v => v.InstanceId));

        // さらに読み直しても増えない。
        harness.SetNow(SessionStart.AddMinutes(5));
        harness.Engine.Update();
        Assert.Equal(2, harness.Visits.Count);
    }

    [Fact]
    public void T10_不正なUTF8を含む行からは訪問を作らない()
    {
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        var broken = Encoding.UTF8.GetBytes(LogText.Joining(SessionStart.AddMinutes(3), Loc.GroupPublic("222")));
        broken[^5] = 0xC3; // 不完全なマルチバイト列を埋め込む
        TempLogDirectory.AppendBytes(file, broken);
        TempLogDirectory.Append(file, LogText.Joined(SessionStart.AddMinutes(3)));

        harness.SetNow(SessionStart.AddMinutes(4));
        harness.Engine.Update();

        Assert.Single(harness.Visits);
        Assert.DoesNotContain(harness.Visits, v => v.InstanceId == "222");
    }

    [Fact]
    public void T10_短縮されたログは読み直して二重加算しない()
    {
        using var dir = new TempLogDirectory();
        var content = LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(2), Loc.GroupPublic("222"), "B");
        var file = dir.WriteSession(SessionStart, content);

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(3), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.Equal(2, harness.Visits.Count);

        var counter = harness.Engine.Counter;
        var before = counter.Counts.Values.Sum();

        // 先頭は同じまま短くなる（書き込み途中の切り詰め）。
        File.WriteAllText(file, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"), new UTF8Encoding(false));

        harness.SetNow(SessionStart.AddMinutes(4));
        harness.Engine.Update();
        harness.SetNow(SessionStart.AddMinutes(5));
        harness.Engine.Update();

        Assert.Equal(before, harness.Engine.Counter.Counts.Values.Sum());
        Assert.Equal(["111"], harness.Snapshot().History.Select(v => v.InstanceId));
    }

    [Fact]
    public void T11_5分間ログが更新されなくても表示を維持する()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.True(harness.Snapshot().ContentReady);

        for (var minute = 3; minute <= 7; minute++)
        {
            harness.SetNow(SessionStart.AddMinutes(minute));
            harness.Engine.Update();
        }

        var snapshot = harness.Snapshot();
        Assert.True(snapshot.ContentReady);
        Assert.Equal(PresenceState.InTarget, snapshot.Presence);
    }

    [Fact]
    public void T12_終了ログなしでプロセスが終われば非表示にする()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.True(harness.Snapshot().ContentReady);

        harness.SetNow(SessionStart.AddMinutes(3));
        harness.Processes.Exit(harness.Utc(SessionStart.AddMinutes(3)));
        harness.Engine.Update();

        var snapshot = harness.Snapshot();
        Assert.False(snapshot.ClientRunning);
        Assert.False(snapshot.ContentReady);
        Assert.Single(snapshot.History); // 履歴は残る
    }

    [Fact]
    public void T12_新しい起動では古い現在地を流用しない()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        harness.SetNow(SessionStart.AddMinutes(3));
        harness.Processes.Exit(harness.Utc(SessionStart.AddMinutes(3)));
        harness.Engine.Update();

        // 新しい起動。ログセッションはまだ入室していない。
        var restart = SessionStart.AddMinutes(10);
        dir.WriteSession(restart, LogText.Noise(restart.AddSeconds(5)));

        harness.SetNow(restart.AddMinutes(1));
        harness.Processes.Start(Process(restart, pid: 5555), harness.Utc(restart));
        harness.Engine.Update();

        var snapshot = harness.Snapshot();

        Assert.True(snapshot.ClientRunning);
        Assert.Equal(PresenceState.Unknown, snapshot.Presence);
        Assert.False(snapshot.ContentReady);
        Assert.Single(snapshot.History); // 期限内の履歴は保持する
    }

    [Fact]
    public void T27_終了前の未読の成功も一度だけ反映して非表示を保つ()
    {
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.Single(harness.Visits);

        // まだ読んでいない成功と終了ログを書いてから、プロセス終了を通知する。
        TempLogDirectory.Append(file, LogText.Move(SessionStart.AddMinutes(3), Loc.GroupPublic("222"), "B"));
        TempLogDirectory.Append(file, LogText.Quit(SessionStart.AddMinutes(4)));

        harness.SetNow(SessionStart.AddMinutes(4));
        harness.Processes.Exit(harness.Utc(SessionStart.AddMinutes(4)));
        harness.Engine.Update();

        var snapshot = harness.Snapshot();

        Assert.Equal(2, harness.Visits.Count);
        Assert.Equal(["111", "222"], snapshot.History.Select(v => v.InstanceId));
        Assert.False(snapshot.ContentReady); // 表示は再開しない

        // もう一度読み直しても増えない。
        harness.SetNow(SessionStart.AddMinutes(5));
        harness.Engine.Update();
        Assert.Equal(2, harness.Visits.Count);
    }

    [Fact]
    public void T15_アプリだけ再起動しても回数と各行のnを保つ()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(2), Loc.GroupPublic("111"), "A"));

        var checkpointPath = Path.Combine(dir.Path, "counter-state.json");
        var checkpoint = new CheckpointStore(checkpointPath);

        List<int?> firstOrdinals;
        using (var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(3), Process(SessionStart), checkpoint))
        {
            harness.Engine.Initialize();
            firstOrdinals = harness.Snapshot().History.Select(v => v.VisitOrdinal).ToList();
        }

        Assert.True(File.Exists(checkpointPath));

        using var restarted = new EngineHarness(dir.Path, SessionStart.AddMinutes(4), Process(SessionStart), new CheckpointStore(checkpointPath));
        restarted.Engine.Initialize();

        var snapshot = restarted.Snapshot();

        Assert.Equal(2, snapshot.History.Count);
        Assert.Equal(firstOrdinals, snapshot.History.Select(v => v.VisitOrdinal));
        Assert.Equal(2, restarted.Engine.Counter.Counts.Values.Single());
    }

    [Fact]
    public void T17_ログが消えてもチェックポイントの回数を引き継ぐ()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var checkpointPath = Path.Combine(dir.Path, "counter-state.json");

        // 1回目の起動: 2回訪問して終了する。
        var firstFile = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(2), Loc.GroupPublic("111"), "A")
            + LogText.Quit(SessionStart.AddMinutes(3)));

        using (var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(3), Process(SessionStart), new CheckpointStore(checkpointPath)))
        {
            harness.Engine.Initialize();
            harness.SetNow(SessionStart.AddMinutes(3));
            harness.Processes.Exit(harness.Utc(SessionStart.AddMinutes(3)));
            harness.Engine.Update();
        }

        // VRChatが古いログを削除した状態で、30分後に起動し直す。
        File.Delete(firstFile);

        var restart = SessionStart.AddMinutes(33);
        dir.WriteSession(restart, LogText.Visit(restart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var second = new EngineHarness(dir.Path, restart.AddMinutes(2), Process(restart, pid: 777), new CheckpointStore(checkpointPath));
        second.Engine.Initialize();

        var snapshot = second.Snapshot();

        // 消えたログの行は捏造しない。
        Assert.Single(snapshot.History);

        // 回数は引き継ぐ（3回目になる）。
        Assert.Equal(3, snapshot.History[0].VisitOrdinal);
    }

    [Fact]
    public void 自動リセットのあとに起動すると回数を1から数える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var checkpointPath = Path.Combine(dir.Path, "counter-state.json");

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Quit(SessionStart.AddMinutes(2)));

        using (var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart), new CheckpointStore(checkpointPath)))
        {
            harness.Engine.Initialize();
            harness.Processes.Exit(harness.Utc(SessionStart.AddMinutes(2)));
            harness.Engine.Update();
        }

        // 退出（終了）から60分で自動リセットが入る。このアプリが動いていない間でも、そのあとの訪問は1から数える（→実装メモ5.110）。
        var restart = SessionStart.AddMinutes(2 + 61);
        dir.WriteSession(restart, LogText.Visit(restart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var second = new EngineHarness(dir.Path, restart.AddMinutes(2), Process(restart, pid: 777), new CheckpointStore(checkpointPath));
        second.Engine.Initialize();

        Assert.Equal(1, second.Snapshot().History.Single().VisitOrdinal);
    }

    [Fact]
    public void 自動リセットの前なら_起動の間が60分を超えても回数を引き継ぐ()
    {
        // リセットまでの時間を長くして、3時間後に起動し直しても自動リセットが入っていない状態にする（→実装メモ5.110）。
        static EngineOptions Long(EngineOptions o) => o with { Retention = TimeSpan.FromMinutes(HistoryStore.MaxRetentionMinutes) };

        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var checkpointPath = Path.Combine(dir.Path, "counter-state.json");

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Quit(SessionStart.AddMinutes(2)));

        using (var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart), new CheckpointStore(checkpointPath), Long))
        {
            harness.Engine.Initialize();
            harness.Processes.Exit(harness.Utc(SessionStart.AddMinutes(2)));
            harness.Engine.Update();
        }

        var restart = SessionStart.AddHours(3);
        dir.WriteSession(restart, LogText.Visit(restart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var second = new EngineHarness(dir.Path, restart.AddMinutes(2), Process(restart, pid: 777), new CheckpointStore(checkpointPath), Long);
        second.Engine.Initialize();

        Assert.Equal([1, 2], second.Snapshot().History.Select(r => r.VisitOrdinal));
    }

    [Fact]
    public void T24_チェックポイントの前にJoining後に成功があっても1回だけ数える()
    {
        using var dir = new TempLogDirectory();
        var joinAt = SessionStart.AddMinutes(1);
        var beforeSuccess = LogText.Entering(joinAt, "A") + LogText.Joining(joinAt, Loc.GroupPublic("111"));
        var content = beforeSuccess + LogText.Joined(joinAt);
        var file = dir.WriteSession(SessionStart, content);

        // 適用位置がJoiningの直後（成功行の手前）にあるチェックポイントを作る。
        var appliedOffset = Encoding.UTF8.GetByteCount(beforeSuccess);
        var checkpointPath = Path.Combine(dir.Path, "counter-state.json");
        var time = new LogTimeConverter(EngineHarness.Tokyo);
        time.TryToUtc(SessionStart, out var sessionStartUtc);

        Infrastructure.LogDirectory.TryComputePrefixHash(file, out var hash, out var hashLength);

        var dto = new CheckpointDto
        {
            EpochId = "epoch-test",
            BaselineKnown = true,
            Anchor = new CheckpointDto.AnchorDto
            {
                SourceSessionId = "s0001",
                StartUtc = sessionStartUtc,
                Reason = "test",
            },
            Sources =
            [
                new CheckpointDto.SourceDto
                {
                    SourceSessionId = "s0001",
                    FileName = Path.GetFileName(file),
                    PrefixHash = hash,
                    PrefixLength = hashLength,
                    CreatedUtc = File.GetCreationTimeUtc(file),
                    AppliedOffset = appliedOffset,
                    SessionStartUtc = sessionStartUtc,
                },
            ],
        };

        new CheckpointStore(checkpointPath).Save(dto);

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart), new CheckpointStore(checkpointPath));
        harness.Engine.Initialize();

        var snapshot = harness.Snapshot();

        Assert.Single(snapshot.History);
        Assert.Equal("111", snapshot.History[0].InstanceId);
        Assert.Equal(1, snapshot.History[0].VisitOrdinal);
        Assert.Equal(1, harness.Engine.Counter.Counts.Values.Single());
    }

    [Fact]
    public void T29_保存に失敗しても処理を続けて診断を出す()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        // 保存先と同じ名前のフォルダーを作って、書き込みを失敗させる。
        var checkpointPath = Path.Combine(dir.Path, "counter-state.json");
        Directory.CreateDirectory(checkpointPath);

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart), new CheckpointStore(checkpointPath));
        harness.Engine.Initialize();

        var snapshot = harness.Snapshot();

        Assert.False(snapshot.CheckpointHealthy);
        Assert.Single(snapshot.History); // メモリ上の処理は続く
        Assert.Contains(harness.Diagnostics.Messages, m => m.Contains("チェックポイント"));
    }

    [Fact]
    public void 複数のVRChatプロセスがあれば自動選択せず非表示にする()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        var time = new LogTimeConverter(EngineHarness.Tokyo);
        time.TryToUtc(SessionStart, out var startUtc);
        time.TryToUtc(SessionStart.AddMinutes(2), out var nowUtc);

        var processes = new TwoProcessProvider(
            new ClientProcessInfo(1, startUtc),
            new ClientProcessInfo(2, startUtc));

        var clock = new ManualClock(nowUtc);
        var diagnostics = new CollectingDiagnostics();

        using var engine = new HistoryEngine(
            new EngineOptions { LogDirectory = dir.Path, PersistCheckpoint = false },
            clock,
            time,
            processes,
            diagnostics);

        engine.Initialize();

        var snapshot = engine.Snapshot();

        Assert.Equal(LogHealth.AmbiguousClient, snapshot.Health);
        Assert.False(snapshot.ContentReady);
    }

    private sealed class TwoProcessProvider(params ClientProcessInfo[] processes) : IProcessProvider
    {
        public IReadOnlyList<ClientProcessInfo> Running => processes;

        public void Poll()
        {
        }

        public bool TryDequeue(out ProcessEvent ev)
        {
            ev = default;
            return false;
        }
    }
}
