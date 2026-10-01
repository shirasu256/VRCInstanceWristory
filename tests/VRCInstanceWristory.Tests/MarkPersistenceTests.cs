using System.Text.Json.Nodes;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 目印をインスタンスごとに残し、ログのリセットで消す（2026-09-27のユーザー指定→実装メモ5.54）。
///
/// 2026-09-26に足した「残さない／インスタンスごと／ワールドごと」の選択（→5.44）はやめ、インスタンスごとに <c>marks.json</c> へ残す形だけにした。
/// 残した目印は、アプリを終了しても戻るが、付けてから次のログのリセット（履歴をまとめて消したとき）で消える。
/// </summary>
public class MarkPersistenceTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 26, 21, 0, 0);

    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    /// <summary>同じワールド（A）の 111 と 222、別のワールド（B）の 333 を渡り歩いた1回の起動。</summary>
    private static string ThreeVisits()
        => LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
           + LogText.Move(SessionStart.AddMinutes(5), Loc.GroupPublic("222"), "A")
           + LogText.Move(SessionStart.AddMinutes(9), Loc.Public("333"), "B");

    private static EngineHarness Harness(TempLogDirectory dir, string marksPath, DateTime nowLocal)
        => new(dir.Path, nowLocal, Process(SessionStart), marks: new MarkFile(marksPath));

    [Fact]
    public void 保存の読み書きで目印と付けた時刻が戻る()
    {
        using var tempFile = new TempFile("marks");
        var path = tempFile.Path;
        var markedAt = new DateTime(2026, 9, 27, 1, 23, 45, DateTimeKind.Utc);

        var file = new MarkFile(path);
        Assert.True(file.Save(new Dictionary<string, MarkEntry>
        {
            ["w:111"] = new(InstanceMark.Heart, markedAt),
            ["w:222"] = new(InstanceMark.None, markedAt),
        }));

        var loaded = new MarkFile(path).Load();
        Assert.Equal(new MarkEntry(InstanceMark.Heart, markedAt), loaded["w:111"]);

        // 目印なしは書かない。
        Assert.False(loaded.ContainsKey("w:222"));

        // 人が読める名前で書く。
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(MarkFile.CurrentSchemaVersion, (int)root["schemaVersion"]!);
        Assert.Equal("Heart", (string?)root["instances"]!["w:111"]!["mark"]);
    }

    /// <summary>2026-09-26の版（形式1）は、目印の名前だけとワールドごとの欄を持っていた。</summary>
    [Fact]
    public void 前の版の保存も読む_付けた時刻はファイルを書いた時刻_ワールドの欄は読まない()
    {
        using var tempFile = new TempFile("marks");
        var path = tempFile.Path;

        File.WriteAllText(path, """
            { "schemaVersion": 1, "instances": { "w:111": "Check" }, "worlds": { "wrld_x": "Heart" } }
            """);

        var written = File.GetLastWriteTimeUtc(path);
        var loaded = new MarkFile(path).Load();

        var entry = Assert.Single(loaded);
        Assert.Equal("w:111", entry.Key);
        Assert.Equal(new MarkEntry(InstanceMark.Check, written), entry.Value);
    }

    [Fact]
    public void 壊れた保存は目印なしで始める()
    {
        using var tempFile = new TempFile("marks");
        var path = tempFile.Path;

        File.WriteAllText(path, "{ broken");
        var file = new MarkFile(path);

        Assert.Empty(file.Load());
        Assert.NotNull(file.LastError);
    }

    [Fact]
    public void 前の版の設定_markPersistence_が残っていても警告なしで読める()
    {
        using var tempFile = new TempFile("marks");
        var path = tempFile.Path;

        File.WriteAllText(path, """{ "markPersistence": "world" }""");

        var log = new CollectingDiagnostics();
        AppSettings.Load(path, log);

        Assert.DoesNotContain(log.Messages, m => m.StartsWith("WARN") || m.StartsWith("ERROR"));
    }

    [Fact]
    public void 目印はアプリを終了しても戻り_同じワールドの別のインスタンスには付かない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, ThreeVisits());
        using var tempFile = new TempFile("marks");
        var path = tempFile.Path;

        string locationKey;

        using (var first = Harness(dir, path, SessionStart.AddMinutes(10)))
        {
            first.Engine.Initialize();
            var visit = first.Snapshot().History[0];
            locationKey = visit.LocationKey;
            first.Engine.SetMark(visit.EventId, InstanceMark.Heart);
        }

        Assert.True(File.Exists(path));

        using var second = Harness(dir, path, SessionStart.AddMinutes(11));
        second.Engine.Initialize();

        var marks = second.Snapshot().Marks;
        Assert.Equal(InstanceMark.Heart, marks[locationKey]);

        // 同じワールドの別のインスタンス（222）には付かない。
        Assert.Single(marks);
    }

    [Fact]
    public void ログのリセットで目印も消え_同じインスタンスへ戻っても付いていない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));
        using var tempFile = new TempFile("marks");
        var path = tempFile.Path;

        using var harness = Harness(dir, path, SessionStart.AddMinutes(2));
        harness.Engine.Initialize();

        var visit = harness.Snapshot().History[0];
        harness.Engine.SetMark(visit.EventId, InstanceMark.Check);

        // 退出から60分の手前では、行も目印も残っている。
        var leftAt = SessionStart.AddMinutes(3);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));
        harness.SetNow(leftAt.AddMinutes(59));
        harness.Engine.Update();
        Assert.Equal(InstanceMark.Check, harness.Snapshot().Marks[visit.LocationKey]);

        // 60分で履歴がまとめて消えると、目印も消える（保存からも）。
        harness.SetNow(leftAt.AddMinutes(61));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);
        Assert.Empty(harness.Snapshot().Marks);
        Assert.Empty(new MarkFile(path).Load());

        // あとで同じインスタンスへ戻っても、目印は付いていない。
        TempLogDirectory.Append(file, LogText.Visit(leftAt.AddMinutes(62), Loc.GroupPublic("111"), "A"));
        harness.SetNow(leftAt.AddMinutes(63));
        harness.Engine.Update();

        Assert.Single(harness.Snapshot().History);
        Assert.Empty(harness.Snapshot().Marks);
    }

    [Fact]
    public void アプリを止めている間にログのリセットがあれば_次の起動で目印は消える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));
        using var tempFile = new TempFile("marks");
        var path = tempFile.Path;

        using (var first = Harness(dir, path, SessionStart.AddMinutes(2)))
        {
            first.Engine.Initialize();
            first.Engine.SetMark(first.Snapshot().History[0].EventId, InstanceMark.Heart);
        }

        Assert.Single(new MarkFile(path).Load());

        // 退出してから、アプリを止めたまま60分が過ぎた。
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));

        using var second = Harness(dir, path, leftAt.AddMinutes(61));
        second.Engine.Initialize();

        Assert.Empty(second.Snapshot().History);
        Assert.Empty(second.Snapshot().Marks);
        Assert.Empty(new MarkFile(path).Load());
    }

    [Fact]
    public void 記録する種類を切り替えて行が消えても_目印は消えない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, ThreeVisits());
        using var tempFile = new TempFile("marks");
        var path = tempFile.Path;

        using var harness = Harness(dir, path, SessionStart.AddMinutes(10));
        harness.Engine.Initialize();

        var visit = harness.Snapshot().History[0];
        harness.Engine.SetMark(visit.EventId, InstanceMark.Warning);

        // Group Public を記録しない設定にすると、111 の行は消える（区切りは進まない＝ログのリセットではない）。
        harness.Engine.SetTargetTypes(new HashSet<AccessType> { AccessType.Public });
        Assert.DoesNotContain(harness.Snapshot().History, r => r.LocationKey == visit.LocationKey);

        // 戻すと、目印の付いたまま出る。
        harness.Engine.SetTargetTypes(TargetAccessTypes.Default);
        Assert.Contains(harness.Snapshot().History, r => r.LocationKey == visit.LocationKey);
        Assert.Equal(InstanceMark.Warning, harness.Snapshot().Marks[visit.LocationKey]);
    }

    /// <summary>
    /// 退出してから付けた目印でも、保持時間を短くしてその行が消えたら一緒に消える
    /// （区切りは「最後の退出 + 保持時間」なので、付けた時刻より前になることがある）。
    /// </summary>
    [Fact]
    public void 保持時間を短くして行が消えたときも_目印は一緒に消える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var leftAt = SessionStart.AddMinutes(3);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A") + LogText.LeftRoom(leftAt));
        using var tempFile = new TempFile("marks");
        var path = tempFile.Path;

        using var harness = Harness(dir, path, leftAt.AddMinutes(20));
        harness.Engine.Initialize();

        harness.Engine.SetMark(harness.Snapshot().History[0].EventId, InstanceMark.Heart);
        Assert.Single(harness.Snapshot().Marks);

        harness.Engine.SetRetention(TimeSpan.FromMinutes(10));

        Assert.Empty(harness.Snapshot().History);
        Assert.Empty(harness.Snapshot().Marks);
    }

    [Fact]
    public void 区切りより前に付けた目印と_行のないインスタンスの目印を消す()
    {
        var cutoff = new DateTime(2026, 9, 27, 3, 0, 0, DateTimeKind.Utc);
        var marks = new MarkStore();

        marks.Set("w:before", InstanceMark.Heart, cutoff.AddMinutes(-1));
        marks.Set("w:after", InstanceMark.Check, cutoff.AddMinutes(1));
        marks.Set("w:gone", InstanceMark.Warning, cutoff.AddMinutes(1));

        Assert.True(marks.RemoveStale(cutoff, ["w:before", "w:after"]));
        Assert.Equal(InstanceMark.None, marks.Get("w:before"));
        Assert.Equal(InstanceMark.Check, marks.Get("w:after"));
        Assert.Equal(InstanceMark.None, marks.Get("w:gone"));

        // 消すものがなければ false。
        Assert.False(marks.RemoveStale(cutoff, ["w:after"]));
    }
}
