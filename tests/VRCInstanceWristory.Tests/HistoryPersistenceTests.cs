using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 訪問履歴の保存（history.json）と、行の上限（500件・30日）（2026-10-01のユーザー指定→実装メモ5.108）。
/// 自動リセットが無効なら、上限に達しない行はアプリや PC を再起動しても（VRChat がログを消しても）消えない。
/// </summary>
public class HistoryPersistenceTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    private sealed class Files : IDisposable
    {
        private readonly TempDirectory _dir = new("history");

        public string CheckpointPath => Path.Combine(_dir.Path, "counter-state.json");

        public string HistoryPath => Path.Combine(_dir.Path, "history.json");

        public CheckpointStore Checkpoint() => new(CheckpointPath);

        public HistoryFile History() => new(HistoryPath);

        public void Dispose() => _dir.Dispose();
    }

    private static Func<EngineOptions, EngineOptions> AutoResetOff => o => o with { AutoReset = false };

    private static EngineHarness Start(TempLogDirectory dir, Files files, DateTime nowLocal, ClientProcessInfo? process, bool autoReset = false, bool checkpoint = true)
    {
        var harness = new EngineHarness(dir.Path, nowLocal, process, checkpoint ? files.Checkpoint() : null, autoReset ? null : AutoResetOff, history: files.History());
        harness.Engine.Initialize();
        harness.Engine.Update();
        return harness;
    }

    /// <summary>VRChat を終えた1回ぶんのログ（対象のインスタンスへ入って出て、正常に終える）。消したファイルの場所を返す。</summary>
    private static string WriteFinishedSession(TempLogDirectory dir, DateTime start, string id)
    {
        var file = dir.WriteSession(start, LogText.Visit(start.AddMinutes(1), Loc.GroupPublic(id), "A", people: 3)
            + LogText.LeftRoom(start.AddMinutes(20)) + LogText.Quit(start.AddMinutes(21)));
        File.SetLastWriteTimeUtc(file, Utc(start.AddMinutes(21)));
        return file;
    }

    [Fact]
    public void 自動リセットが無効なら_ログが消えても再起動後に行が戻る()
    {
        using var dir = new TempLogDirectory();
        using var files = new Files();
        var file = WriteFinishedSession(dir, SessionStart, "111");

        DateTime? leftAt;
        int? ordinal;

        using (var first = Start(dir, files, SessionStart.AddMinutes(30), null))
        {
            var row = Assert.Single(first.Snapshot().History);
            leftAt = row.LeftAtUtc;
            ordinal = row.VisitOrdinal;
        }

        Assert.True(File.Exists(files.HistoryPath));

        // VRChat が24時間より古いログを消したあと、3日後に起動し直す。
        File.Delete(file);

        using var second = Start(dir, files, SessionStart.AddDays(3), null);
        var restored = Assert.Single(second.Snapshot().History);
        Assert.Equal("111", restored.InstanceId);
        Assert.Equal(leftAt, restored.LeftAtUtc);
        Assert.Equal(ordinal, restored.VisitOrdinal);
        Assert.Equal(3, restored.PeopleCount);
    }

    [Fact]
    public void 自動リセットが有効なら保存から戻さず_従来どおりログから組み直す()
    {
        using var dir = new TempLogDirectory();
        using var files = new Files();
        var file = WriteFinishedSession(dir, SessionStart, "111");

        using (var first = Start(dir, files, SessionStart.AddMinutes(30), null, autoReset: true))
            Assert.Single(first.Snapshot().History);

        File.Delete(file);

        using var second = Start(dir, files, SessionStart.AddMinutes(40), null, autoReset: true);
        Assert.Empty(second.Snapshot().History);
    }

    [Fact]
    public void ログが残っていれば読んだ行へ置き換わり_台帳を読めなくても二重にならない()
    {
        using var dir = new TempLogDirectory();
        using var files = new Files();
        WriteFinishedSession(dir, SessionStart, "111");

        using (var first = Start(dir, files, SessionStart.AddMinutes(30), null))
            Assert.Single(first.Snapshot().History);

        // 台帳（チェックポイント）があれば eventId が同じ。
        using (var second = Start(dir, files, SessionStart.AddHours(2), null))
            Assert.Single(second.Snapshot().History);

        // 台帳がなくソースIDが変わっても、同じ訪問（インスタンスと入室時刻）は1行にまとめる。
        File.Delete(files.CheckpointPath);

        using var third = Start(dir, files, SessionStart.AddHours(3), null, checkpoint: false);
        var row = Assert.Single(third.Snapshot().History);
        Assert.Equal("111", row.InstanceId);
    }

    [Fact]
    public void 自動リセットが無効なら_間の空いた前の起動のログもさかのぼって読む()
    {
        using var dir = new TempLogDirectory();
        using var files = new Files();
        var earlier = SessionStart.AddHours(-5);
        WriteFinishedSession(dir, earlier, "100");

        // 間に、対象のインスタンスへ入らなかった起動を1回挟む（直前の起動は、回数の数え始めを決めるためにいつも読まれる）。
        var middle = SessionStart.AddHours(-2);
        var idle = dir.WriteSession(middle, LogText.Noise(middle) + LogText.Quit(middle.AddMinutes(5)));
        File.SetLastWriteTimeUtc(idle, Utc(middle.AddMinutes(5)));

        var current = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(SessionStart.AddMinutes(5)));
        File.SetLastWriteTimeUtc(current, Utc(SessionStart.AddMinutes(5)));

        // 保存がなくても（この版へ上げた直後など）、残っているログはすべて読む。以前は60分を超える空きでさかのぼるのをやめていた。
        File.Delete(files.HistoryPath);

        using var off = new EngineHarness(dir.Path, SessionStart.AddMinutes(10), Process(SessionStart), configure: AutoResetOff);
        off.Engine.Initialize();
        off.Engine.Update();
        Assert.Equal(["100", "111"], off.Snapshot().History.Select(r => r.InstanceId));

        using var on = new EngineHarness(dir.Path, SessionStart.AddMinutes(10), Process(SessionStart));
        on.Engine.Initialize();
        on.Engine.Update();
        Assert.Equal(["111"], on.Snapshot().History.Select(r => r.InstanceId));
    }

    [Fact]
    public void 滞在中のまま保存した行は_ログが消えていれば終わりを入れる()
    {
        using var dir = new TempLogDirectory();
        using var files = new Files();
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));
        File.SetLastWriteTimeUtc(file, Utc(SessionStart.AddMinutes(1)));

        using (var first = Start(dir, files, SessionStart.AddMinutes(30), Process(SessionStart)))
            Assert.Null(Assert.Single(first.Snapshot().History).LeftAtUtc);

        // VRChat ごと PC を落とし、ログが消えたあとで起動する。
        File.Delete(file);

        using var second = Start(dir, files, SessionStart.AddDays(2), null);
        var row = Assert.Single(second.Snapshot().History);
        Assert.NotNull(row.LeftAtUtc);
        Assert.InRange(row.LeftAtUtc!.Value, row.VisitedAtUtc, Utc(SessionStart.AddMinutes(30)));
    }

    [Fact]
    public void 滞在中のまま保存した行は_ログが残っていればログから退出を確かめる()
    {
        using var dir = new TempLogDirectory();
        using var files = new Files();
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using (var first = Start(dir, files, SessionStart.AddMinutes(30), Process(SessionStart)))
            Assert.Null(Assert.Single(first.Snapshot().History).LeftAtUtc);

        // このアプリを止めている間に退出して終了し、そのあと別の起動が2回あった（このファイルは最新でも直近でもない）。
        TempLogDirectory.Append(file, LogText.LeftRoom(SessionStart.AddMinutes(40)) + LogText.Quit(SessionStart.AddMinutes(41)));
        File.SetLastWriteTimeUtc(file, Utc(SessionStart.AddMinutes(41)));

        foreach (var hours in new[] { 3, 6 })
        {
            var later = dir.WriteSession(SessionStart.AddHours(hours), LogText.Noise(SessionStart.AddHours(hours)) + LogText.Quit(SessionStart.AddHours(hours).AddMinutes(5)));
            File.SetLastWriteTimeUtc(later, Utc(SessionStart.AddHours(hours).AddMinutes(5)));
        }

        using var second = Start(dir, files, SessionStart.AddHours(8), null);
        var row = Assert.Single(second.Snapshot().History);
        Assert.Equal(Utc(SessionStart.AddMinutes(40)), row.LeftAtUtc);
        Assert.False(row.EndedByCrash);
    }

    [Fact]
    public void 上限を超えた行は_新しい500件と30日以内だけを残し_再起動しても戻らない()
    {
        using var dir = new TempLogDirectory();
        using var files = new Files();
        var now = Utc(SessionStart);

        // 1時間おきに600件。一番古いものは25日前。さらに40日前の行を1件混ぜる。
        var records = Enumerable.Range(0, 600).Select(i => Record($"s0001:{i}", $"{i:D5}", now.AddHours(-600 + i))).ToList();
        records.Add(Record("s0001:old", "old", now.AddDays(-40)));
        Assert.True(files.History().Save(records, now));

        using (var harness = Start(dir, files, SessionStart, null))
        {
            var history = harness.Snapshot().History;
            Assert.Equal(HistoryStore.MaxRecords, history.Count);
            Assert.Equal("00100", history[0].InstanceId);
            Assert.Equal("00599", history[^1].InstanceId);

            // 30日を過ぎた行も消える。
            harness.SetNow(SessionStart.AddDays(30).AddHours(-300).AddMinutes(1));
            harness.Engine.Update();
            Assert.Equal("00301", harness.Snapshot().History[0].InstanceId);
        }

        // 消した区切りは保存してあるので、保存の中身が古くても戻らない。
        Assert.True(files.History().Save(records, now));

        using var restarted = Start(dir, files, SessionStart.AddDays(30).AddHours(-300).AddMinutes(1), null);
        Assert.Equal("00301", restarted.Snapshot().History[0].InstanceId);
    }

    [Fact]
    public void 上限で消えても目印は残った行のインスタンスのぶんは外さず_直前のリセットにもならない()
    {
        using var dir = new TempLogDirectory();
        using var files = new Files();
        var now = Utc(SessionStart);
        var records = Enumerable.Range(0, HistoryStore.MaxRecords).Select(i => Record($"s0001:{i}", $"{i:D5}", now.AddHours(-HistoryStore.MaxRecords + i))).ToList();
        Assert.True(files.History().Save(records, now));

        using var marksFile = new TempFile("marks");
        using var harness = new EngineHarness(dir.Path, SessionStart, null, files.Checkpoint(), AutoResetOff, new Core.Marks.MarkFile(marksFile.Path), history: files.History());
        harness.Engine.Initialize();
        harness.Engine.Update();

        // 一番古い行と、次に消える2番目の行に目印を付ける。
        harness.Engine.SetMark("s0001:0", Core.Marks.InstanceMark.Heart);
        harness.Engine.SetMark("s0001:1", Core.Marks.InstanceMark.Check);

        // 1件消えるところまで進める（いちばん古い行が30日を過ぎる）。
        harness.SetNow(SessionStart.AddDays(30).AddHours(-HistoryStore.MaxRecords).AddMinutes(1));
        harness.Engine.Update();

        Assert.Equal(HistoryStore.MaxRecords - 1, harness.Snapshot().History.Count);
        Assert.False(harness.Snapshot().Marks.ContainsKey(records[0].LocationKey));
        Assert.Equal(Core.Marks.InstanceMark.Check, harness.Snapshot().Marks[records[1].LocationKey]);
        Assert.False(harness.Engine.CanUndoClearHistory);
    }

    [Fact]
    public void ログの消えた行も_記録する種類を変えたときと直前のリセットを戻したときに残る()
    {
        using var dir = new TempLogDirectory();
        using var files = new Files();
        var file = WriteFinishedSession(dir, SessionStart, "111");

        using (var first = Start(dir, files, SessionStart.AddMinutes(30), null))
            Assert.Single(first.Snapshot().History);

        File.Delete(file);

        using var second = Start(dir, files, SessionStart.AddDays(1), null);
        Assert.Single(second.Snapshot().History);

        // 残す種類のままなら残る。外した種類の行は消える。
        second.Engine.SetTargetTypes(new HashSet<AccessType> { AccessType.GroupPublic, AccessType.Public });
        Assert.Single(second.Snapshot().History);

        second.Engine.ClearHistory();
        Assert.Empty(second.Snapshot().History);

        second.Engine.UndoClearHistory();
        Assert.Equal("111", Assert.Single(second.Snapshot().History).InstanceId);

        second.Engine.SetTargetTypes(new HashSet<AccessType> { AccessType.Public });
        Assert.Empty(second.Snapshot().History);
    }

    [Fact]
    public void 訪問履歴の保存は一緒にいた人と写真を含めて読み戻せ_壊れていれば読まない()
    {
        using var file = new TempFile("history");
        var record = Record("s0003:42", "07254", new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
        record.LeftAtUtc = record.VisitedAtUtc.AddMinutes(30);
        record.EndedByCrash = true;
        record.CrashedAfter = true;
        record.VisitOrdinal = 2;
        record.Companions.Add(new Companion("usr_1", "相手", record.VisitedAtUtc.AddMinutes(1), null));
        record.Photos.Add(new VisitPhoto(@"C:\Pictures\VRChat\a.png", record.VisitedAtUtc.AddMinutes(2)));

        var store = new HistoryFile(file.Path);
        Assert.True(store.Save([record], record.LeftAtUtc.Value));

        var loaded = store.Load();
        Assert.NotNull(loaded);
        var back = Assert.Single(loaded.Records);
        Assert.Equal(record.ToString(), back.ToString());
        Assert.Equal(DateTimeKind.Utc, back.VisitedAtUtc.Kind);
        Assert.Equal(record.LeftAtUtc, back.LeftAtUtc);
        Assert.True(back.EndedByCrash);
        Assert.True(back.CrashedAfter);
        Assert.Equal(record.Companions, back.Companions);
        Assert.Equal(record.Photos, back.Photos);
        Assert.Equal(record.Location, back.Location);
        Assert.Equal(AccessType.GroupPublic, back.AccessType);

        File.WriteAllText(file.Path, "{ broken");
        Assert.Null(store.Load());
        Assert.NotNull(store.LastError);
    }

    private static VisitRecord Record(string eventId, string instanceId, DateTime visitedAtUtc)
    {
        var location = Loc.GroupPublic(instanceId);
        var parsed = LocationParser.Parse(location);

        return new VisitRecord
        {
            EventId = eventId,
            SourceSessionId = eventId.Split(':')[0],
            SuccessByteOffset = 0,
            SessionOrder = 0,
            LocationKey = parsed.LocationKey,
            WorldId = Loc.WorldA,
            InstanceId = instanceId,
            AccessType = AccessType.GroupPublic,
            WorldName = "A",
            GroupId = Loc.GroupA,
            Location = location,
            VisitedAtUtc = visitedAtUtc,
            LeftAtUtc = visitedAtUtc.AddMinutes(30),
        };
    }
}
