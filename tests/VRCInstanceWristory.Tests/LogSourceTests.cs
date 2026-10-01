using System.Text;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// ログのファイルとソースの対応づけ（差し替え・台帳の掃除・先頭のハッシュの使い回し）と、
/// 読み取りの失敗として扱う例外の範囲。
/// </summary>
public class LogSourceTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    private static int CountMessages(EngineHarness harness, string text)
        => harness.Diagnostics.Messages.Count(m => m.Contains(text, StringComparison.Ordinal));

    /// <summary>走査の間隔（2秒）を超えて時計を進めながら、何回か更新する。</summary>
    private static void ScanRepeatedly(EngineHarness harness, DateTime fromLocal, int times)
    {
        for (var i = 1; i <= times; i++)
        {
            harness.SetNow(fromLocal.AddSeconds(3 * i));
            harness.Engine.Update();
        }
    }

    /// <summary>
    /// 同じ名前のファイルが先頭から書き換わったら、旧ソースを捨てて新しいソースを1つだけ作る。
    /// 以前は照合が旧ソースに当たって毎回「新しいソース」になり、走査（2秒ごと）のたびにソースが増えていた。
    /// </summary>
    [Fact]
    public void 差し替えられたログは一度だけ新しいソースになり旧ソースの行は消える()
    {
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        var now = SessionStart.AddMinutes(10);
        using var harness = new EngineHarness(dir.Path, now, Process(SessionStart));
        harness.Engine.Initialize();

        Assert.Equal("111", Assert.Single(harness.Snapshot().History).InstanceId);
        var createdBefore = CountMessages(harness, "新しいログソース");

        // 先頭から別の内容で書き直す（作成日時は同じまま）。
        File.WriteAllText(
            file,
            LogText.Noise(SessionStart) + LogText.Visit(SessionStart.AddMinutes(3), Loc.GroupPublic("222"), "B"),
            new UTF8Encoding(false));

        ScanRepeatedly(harness, now, times: 5);

        Assert.Equal(1, CountMessages(harness, "ログの先頭が変化しました"));
        Assert.Equal(createdBefore + 1, CountMessages(harness, "新しいログソース"));
        Assert.Equal("222", Assert.Single(harness.Snapshot().History).InstanceId);
    }

    /// <summary>
    /// ファイルがなく、いまのカウント期間より前に始まった記録は、次の起動のときに台帳（とチェックポイント）から捨てる。
    /// 起点の直前の1件（起点をそこに決めた根拠）とファイルの残っているものは残す。
    /// 動いている間は捨てない（フォルダーが一時的に読めないだけのことがあるため）。
    /// </summary>
    [Fact]
    public void カウント期間より前のファイルのない記録は捨てる()
    {
        using var dir = new TempLogDirectory();
        var path = Path.Combine(dir.Path, "counter-state.json");

        var oldest = SessionStart.AddHours(-9);
        var older = SessionStart.AddHours(-6);
        var previous = SessionStart.AddHours(-3);
        var oldestFile = dir.WriteSession(oldest, LogText.Noise(oldest) + LogText.Quit(oldest.AddMinutes(30)));
        var olderFile = dir.WriteSession(older, LogText.Noise(older) + LogText.Quit(older.AddMinutes(30)));
        var previousFile = dir.WriteSession(previous, LogText.Noise(previous) + LogText.Quit(previous.AddMinutes(30)));
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        // 前の起動のログは、それぞれ終わったときに書き終えている（直近に書かれたものとして読み直さない）。
        foreach (var (file, start) in new[] { (oldestFile, oldest), (olderFile, older), (previousFile, previous) })
            File.SetLastWriteTimeUtc(file, TestProcesses.Utc(start.AddMinutes(30)));

        var now = SessionStart.AddMinutes(10);

        using (var harness = new EngineHarness(dir.Path, now, Process(SessionStart), new CheckpointStore(path)))
        {
            harness.Engine.Initialize();

            // VRChat が古いログを消した。動いている間は、ないと分かっても捨てない。
            File.Delete(oldestFile);
            File.Delete(olderFile);
            File.Delete(previousFile);

            ScanRepeatedly(harness, now, times: 2);
        }

        Assert.Equal(4, new CheckpointStore(path).Load()!.Sources.Count);

        // 次の起動で捨てる。
        using (var harness = new EngineHarness(dir.Path, now.AddMinutes(1), Process(SessionStart), new CheckpointStore(path)))
            harness.Engine.Initialize();

        var saved = new CheckpointStore(path).Load();
        Assert.NotNull(saved);

        var names = saved.Sources.Select(s => s.FileName).ToList();
        Assert.DoesNotContain(Path.GetFileName(oldestFile), names);
        Assert.DoesNotContain(Path.GetFileName(olderFile), names);
        Assert.Contains(Path.GetFileName(previousFile), names); // 起点の直前は残す
        Assert.Equal(2, names.Count);
    }

    /// <summary>
    /// 大きさと更新時刻が変わらない間は、先頭を読み直さずに覚えていたハッシュを使う。
    /// ほかのプロセスが共有を許さずに開いていても、覚えていれば答えられることで確かめる。
    /// </summary>
    [Fact]
    public void 変わっていないファイルの先頭は読み直さない()
    {
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));
        var cache = new PrefixHashCache();

        Assert.True(cache.TryGet(file, out var first, out var length));

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(LogDirectory.TryComputePrefixHash(file, out _, out _));
            Assert.True(cache.TryGet(file, out var cached, out var cachedLength));
            Assert.Equal(first, cached);
            Assert.Equal(length, cachedLength);
        }

        // 書き足されたら（大きさが変わったら）読み直す。先頭が同じなので同じハッシュになる。
        TempLogDirectory.Append(file, LogText.Noise(SessionStart.AddMinutes(2)));
        Assert.True(cache.TryGet(file, out var grown, out _));
        Assert.Equal(first, grown);

        // 覚えていないファイルは、開けなければ答えない。
        cache.Retain([]);

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(cache.TryGet(file, out _, out _));
    }

    /// <summary>
    /// 行の解析・履歴の更新で起きた例外（プログラムの誤り）でも、エンジンは止めない（同じ行で起動のたびに止まり、起動できなくなるため）。
    /// パネルは読み取りの失敗と同じく隠し、原因はスタックトレースごと1回だけ記録する。
    /// </summary>
    [Fact]
    public void 行の処理で例外が起きても止めずに原因を1回だけ記録する()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(10), Process(SessionStart));
        var log = harness.Diagnostics;
        harness.Engine.VisitAdded += _ => throw new InvalidOperationException("誤り");

        harness.Engine.Initialize();
        ScanRepeatedly(harness, SessionStart.AddMinutes(10), times: 3);

        Assert.Equal(LogHealth.ReadError, harness.Snapshot().Health);
        var errors = log.Messages.Where(m => m.Contains("プログラムの誤り")).ToList();
        Assert.Single(errors);
        Assert.Contains("InvalidOperationException", errors[0]);
        Assert.Contains(" at ", errors[0]); // スタックトレースも残す
    }

    /// <summary>
    /// 前の版のチェックポイント（ソースごとに <c>EpochId</c> を書いていた）もそのまま読める。
    /// 知らない項目は読み飛ばす。
    /// </summary>
    [Fact]
    public void ソースごとのepochIdを書いた前の版のチェックポイントも読める()
    {
        using var dir = new TempLogDirectory();
        var path = Path.Combine(dir.Path, "counter-state.json");

        File.WriteAllText(path, """
            {
              "SchemaVersion": 1,
              "EpochId": "e1",
              "BaselineKnown": true,
              "Counts": {},
              "Sources": [
                {
                  "SourceSessionId": "s0001",
                  "FileName": "output_log_2026-09-11_01-00-00.txt",
                  "PrefixLength": 0,
                  "AppliedOffset": 0,
                  "SessionStartUtc": "2026-09-10T16:00:00Z",
                  "EpochId": "e1",
                  "FileMissing": true
                }
              ],
              "RecentEvents": []
            }
            """);

        var store = new CheckpointStore(path);
        var dto = store.Load();

        Assert.Null(store.LastError);
        Assert.NotNull(dto);
        Assert.Equal("s0001", Assert.Single(dto.Sources).SourceSessionId);
    }
}
