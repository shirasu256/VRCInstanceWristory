using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 「カウント延長」を保存する（2026-09-26のユーザー指定→実装メモ5.45）。
///
/// それまでは押した時刻をメモリにしか持たず、アプリを再起動すると延長が失われ、元の期限（退出+60分）を過ぎていれば
/// 起動時の復元で行が消えていた（6節の限界）。押した時刻と、消去の区切りを <c>counter-state.json</c> に残す。
/// </summary>
public class RetentionPersistenceTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 26, 21, 0, 0);

    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    [Fact]
    public void 延長してから再起動しても延長は失われず_元の期限では消えない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(leftAt));
        using var tempFile = new TempFile("checkpoint");
        var path = tempFile.Path;

        var pressedAt = leftAt.AddMinutes(50);

        using (var first = new EngineHarness(dir.Path, pressedAt, Process(SessionStart), new CheckpointStore(path)))
        {
            first.Engine.Initialize();
            first.Engine.ResetRetention();
            Assert.Equal(first.Utc(pressedAt.AddMinutes(60)), first.Snapshot().RetentionDeadlineUtc);
        }

        // 元の期限（退出+60分）を過ぎてから立ち上げ直す。
        using var second = new EngineHarness(dir.Path, leftAt.AddMinutes(70), Process(SessionStart), new CheckpointStore(path));
        second.Engine.Initialize();

        var snapshot = second.Snapshot();
        Assert.Single(snapshot.History);
        Assert.Equal(second.Utc(pressedAt.AddMinutes(60)), snapshot.RetentionDeadlineUtc);

        // 延長した期限でまとめて消える。
        second.SetNow(pressedAt.AddMinutes(60));
        second.Engine.Update();
        Assert.Empty(second.Snapshot().History);
    }

    [Fact]
    public void 延長を保存しなければ再起動で消えていた_比較のため()
    {
        // チェックポイントなし（=保存しない）で同じことをすると、再起動の時点で消える。
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(leftAt));

        using (var first = new EngineHarness(dir.Path, leftAt.AddMinutes(50), Process(SessionStart)))
        {
            first.Engine.Initialize();
            first.Engine.ResetRetention();
        }

        using var second = new EngineHarness(dir.Path, leftAt.AddMinutes(70), Process(SessionStart));
        second.Engine.Initialize();
        Assert.Empty(second.Snapshot().History);
    }

    [Fact]
    public void 消えた行は保持時間を延ばして再起動しても戻らない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(leftAt));
        using var tempFile = new TempFile("checkpoint");
        var path = tempFile.Path;

        using (var first = new EngineHarness(dir.Path, leftAt.AddMinutes(61), Process(SessionStart), new CheckpointStore(path)))
        {
            first.Engine.Initialize();
            Assert.Empty(first.Snapshot().History);
        }

        // 180分にして立ち上げ直すと、区切りを保存していなければ退出+180分まで戻ってしまう。
        using var second = new EngineHarness(
            dir.Path,
            leftAt.AddMinutes(62),
            Process(SessionStart),
            new CheckpointStore(path),
            o => o with { Retention = TimeSpan.FromMinutes(180) });

        second.Engine.Initialize();
        Assert.Empty(second.Snapshot().History);
    }

    [Fact]
    public void もう効かない延長は捨てて保存が伸び続けない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(leftAt));
        using var tempFile = new TempFile("checkpoint");
        var path = tempFile.Path;

        using var harness = new EngineHarness(dir.Path, leftAt.AddMinutes(10), Process(SessionStart), new CheckpointStore(path));
        harness.Engine.Initialize();

        var pressedAt = leftAt.AddMinutes(10);
        harness.Engine.ResetRetention();
        Assert.Single(new CheckpointStore(path).Load()!.RetentionResets);

        // 延長した期限で消え、しばらくして別の訪問をして離れる。
        harness.SetNow(pressedAt.AddMinutes(61));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);

        var again = pressedAt.AddMinutes(850);
        TempLogDirectory.Append(file, LogText.Visit(again, Loc.GroupPublic("222"), "B") + LogText.LeftRoom(again.AddMinutes(5)));
        harness.SetNow(again.AddMinutes(6));
        harness.Engine.Update();

        // その退出から60分で消えたとき、区切りは延長から900分（保持時間の最大→実装メモ5.71）より後になる。
        harness.SetNow(again.AddMinutes(66));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);

        var saved = new CheckpointStore(path).Load()!;
        Assert.Empty(saved.RetentionResets);
        Assert.NotNull(saved.HistoryCutoffUtc);
    }

    [Fact]
    public void 延長の欄がない前の版のチェックポイントもそのまま読める()
    {
        using var tempFile = new TempFile("checkpoint");
        var path = tempFile.Path;

        File.WriteAllText(path, """{ "SchemaVersion": 1, "BaselineKnown": false }""");

        var loaded = new CheckpointStore(path).Load();
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.RetentionResets);
        Assert.Null(loaded.HistoryCutoffUtc);
    }

    [Fact]
    public void 時計が戻って未来に見える延長と区切りは採らない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));
        using var tempFile = new TempFile("checkpoint");
        var path = tempFile.Path;

        var time = new LogTimeConverter(EngineHarness.Tokyo);
        time.TryToUtc(SessionStart.AddDays(1), out var future);

        new CheckpointStore(path).Save(new CheckpointDto
        {
            RetentionResets = [future],
            HistoryCutoffUtc = future,
        });

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart), new CheckpointStore(path));
        harness.Engine.Initialize();

        // 未来の区切りを採ると、いまの行まで消えてしまう。
        Assert.Single(harness.Snapshot().History);
    }
}
