using System.Drawing;
using System.Text.Json.Nodes;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// デスクトップのウィンドウと、そこで変えられるようにした設定（2026-09-26のユーザー指定→実装メモ5.39）。
///
/// - ログリセットまでの分数（既定60分）を変えられる。「カウント延長」もこの分数で数える
/// - 記録するインスタンスの種類を選べる。変えるとログを読み直して一覧を作り直す
/// - ウィンドウの中身は手首のパネルと同じ絵で、マウスで同じ操作（スクロール・目印・延長）ができる
/// </summary>
public class DesktopWindowTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    /// <summary>3時間前に正常終了した起動。これで回数の基準が確定する（→EngineBehaviourTests）。</summary>
    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    // ------------------------------------------------------------------ 保持時間

    [Fact]
    public void 保持時間を30分にすると退出から30分で消える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(
            dir.Path,
            SessionStart.AddMinutes(2),
            Process(SessionStart),
            configure: o => o with { Retention = TimeSpan.FromMinutes(30) });

        harness.Engine.Initialize();

        var leftAt = SessionStart.AddMinutes(3);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));
        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();

        var counting = harness.Snapshot();
        Assert.Equal(harness.Utc(leftAt.AddMinutes(30)), counting.RetentionDeadlineUtc);
        Assert.Equal(TimeSpan.FromMinutes(29), counting.RetentionRemaining(harness.Clock.UtcNow));

        harness.SetNow(leftAt.AddMinutes(29).AddSeconds(59));
        harness.Engine.Update();
        Assert.Single(harness.Snapshot().History);

        harness.SetNow(leftAt.AddMinutes(30));
        harness.Engine.Update();
        Assert.Empty(harness.Snapshot().History);
    }

    [Fact]
    public void 滞在中の残り時間は設定した保持時間で止まる()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(
            dir.Path,
            SessionStart.AddMinutes(2),
            Process(SessionStart),
            configure: o => o with { Retention = TimeSpan.FromMinutes(120) });

        harness.Engine.Initialize();

        var staying = harness.Snapshot();
        Assert.Null(staying.RetentionDeadlineUtc);
        Assert.Equal(TimeSpan.FromMinutes(120), staying.RetentionRemaining(harness.Clock.UtcNow));
        Assert.Equal("2:00:00", Countdown.Format(staying.RetentionRemaining(harness.Clock.UtcNow)));
    }

    [Fact]
    public void 保持時間を実行中に延ばすと期限も延び_短くして期限を過ぎればその場で消える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        var leftAt = SessionStart.AddMinutes(3);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));
        harness.SetNow(leftAt.AddMinutes(40));
        harness.Engine.Update();

        // 退出から40分。90分へ延ばすと、期限は退出+90分になる（数え直しではなく、残りが伸びる）。
        harness.Engine.SetRetention(TimeSpan.FromMinutes(90));
        var longer = harness.Snapshot();
        Assert.Equal(harness.Utc(leftAt.AddMinutes(90)), longer.RetentionDeadlineUtc);
        Assert.Equal(TimeSpan.FromMinutes(50), longer.RetentionRemaining(harness.Clock.UtcNow));
        Assert.Single(longer.History);

        // 60分を過ぎても消えない。
        harness.SetNow(leftAt.AddMinutes(70));
        harness.Engine.Update();
        Assert.Single(harness.Snapshot().History);

        // 退出から70分の時点で30分へ縮めると、期限を過ぎているのでその場で消える。
        harness.Engine.SetRetention(TimeSpan.FromMinutes(30));
        Assert.Empty(harness.Snapshot().History);

        // 一度消えた行は、長くしても戻らない。
        harness.Engine.SetRetention(TimeSpan.FromMinutes(180));
        Assert.Empty(harness.Snapshot().History);
    }

    [Fact]
    public void カウント延長も設定した保持時間だけ延ばす()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(
            dir.Path,
            SessionStart.AddMinutes(2),
            Process(SessionStart),
            configure: o => o with { Retention = TimeSpan.FromMinutes(15) });

        harness.Engine.Initialize();

        var leftAt = SessionStart.AddMinutes(3);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));

        var pressedAt = leftAt.AddMinutes(10);
        harness.SetNow(pressedAt);
        harness.Engine.Update();
        harness.Engine.ResetRetention();

        var extended = harness.Snapshot();
        Assert.Equal(harness.Utc(pressedAt.AddMinutes(15)), extended.RetentionDeadlineUtc);
        Assert.Equal(TimeSpan.FromMinutes(15), extended.RetentionRemaining(harness.Clock.UtcNow));
    }

    [Fact]
    public void 残り時間は900分まで表せ_99分59秒を超えると時分秒で出す()
    {
        // 99:59 までは分と秒。それを超えると 1:40:00 の形（2026-09-27のユーザー指定→実装メモ5.67）。
        Assert.Equal("99:59", Countdown.Format(TimeSpan.FromSeconds((99 * 60) + 59)));
        Assert.Equal("1:40:00", Countdown.Format(TimeSpan.FromMinutes(100)));
        Assert.Equal("1:40:01", Countdown.Format(TimeSpan.FromSeconds((100 * 60) + 0.4)));
        Assert.Equal("2:59:59", Countdown.Format(TimeSpan.FromSeconds((179 * 60) + 59)));
        Assert.Equal("3:00:00", Countdown.Format(TimeSpan.FromMinutes(180)));
        Assert.Equal("15:00:00", Countdown.Format(TimeSpan.FromMinutes(900)));
        Assert.Equal("15:00:00", Countdown.Format(TimeSpan.FromMinutes(2000)));
        Assert.Equal("00:00", Countdown.Format(TimeSpan.FromMinutes(-1)));
        Assert.Equal(TimeSpan.FromMinutes(HistoryStore.MaxRetentionMinutes), Countdown.Limit);
    }

    [Theory]
    [InlineData(100, 99 * 60 + 59, "1:39:59")]
    [InlineData(120, 59 * 60 + 59, "0:59:59")]
    [InlineData(900, 59 * 60, "0:59:00")]
    [InlineData(120, 0, "0:00:00")]
    [InlineData(90, 89 * 60 + 59, "89:59")]
    [InlineData(60, 59 * 60 + 59, "59:59")]
    public void 数え始めが1時間40分以上なら減っても時分秒のまま(int totalMinutes, int remainingSeconds, string expected)
    {
        // 2026-09-28のユーザー指定（→実装メモ5.86）。99:59 や 59:00 へ形を変えない。
        Assert.Equal(expected, Countdown.Format(TimeSpan.FromSeconds(remainingSeconds), TimeSpan.FromMinutes(totalMinutes)));
    }

    [Fact]
    public void 保持時間が100分以上のときパネルへ渡る残り時間は時分秒()
    {
        var target = new RecordingPanelTarget();
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var presenter = new PanelPresenter(target, new LogTimeConverter(TimeZoneInfo.Utc), NullDiagnostics.Instance, new ManualClock(now));

        presenter.Apply(new EngineSnapshot
        {
            Retention = TimeSpan.FromMinutes(120),
            RetentionDeadlineUtc = now.AddMinutes(30),
            ClientRunning = true,
            Presence = Core.Visits.PresenceState.InExcluded,
            Health = LogHealth.Ok,
            History = [],
            MenuPageOpen = true,
            AwaitingViewAngleClose = false,
            BaselineKnown = true,
            CheckpointHealthy = true,
            Generation = 1,
        });

        Assert.Equal("0:30:00", Countdown.Format(target.Remaining, target.Total));
    }

    [Fact]
    public void 滞在時間の棒は保持時間の設定によらず60分で最大になる()
    {
        var visit = new Core.Visits.VisitRecord
        {
            EventId = "e",
            SourceSessionId = "s",
            SuccessByteOffset = 0,
            SessionOrder = 0,
            LocationKey = "k",
            WorldId = Loc.WorldA,
            InstanceId = "1",
            AccessType = AccessType.Public,
            VisitedAtUtc = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc),
            LeftAtUtc = new DateTime(2026, 9, 11, 1, 0, 0, DateTimeKind.Utc),
        };

        Assert.Equal(1f, RowFormatter.StayFraction(visit, visit.LeftAtUtc!.Value));
    }

    // ------------------------------------------------------------------ 記録する種類

    /// <summary>Public → Friends（対象外）→ Public と渡った1回の起動。</summary>
    private static string PublicFriendsPublic()
        => LogText.Visit(SessionStart.AddMinutes(1), Loc.Public("111"), "A")
           + LogText.Move(SessionStart.AddMinutes(5), Loc.Friends("222"), "B")
           + LogText.Move(SessionStart.AddMinutes(10), Loc.Public("333"), "C");

    [Fact]
    public void 記録する種類にFriendsを加えるとログを読み直してFriendsの訪問も行になる()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, PublicFriendsPublic());

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(12), Process(SessionStart));
        harness.Engine.Initialize();

        var before = harness.Snapshot().History;
        Assert.Equal(["111", "333"], before.Select(r => r.InstanceId));

        // 既定では Friends は対象外なので、333 の上に「対象外のインスタンスへ移動」の帯が出る。
        Assert.True(before[1].ExcludedBefore);
        Assert.Equal(1, before[0].VisitOrdinal);
        Assert.Equal(1, before[1].VisitOrdinal);

        var types = new HashSet<AccessType>(TargetAccessTypes.Default) { AccessType.Friends };
        harness.Engine.SetTargetTypes(types);

        var after = harness.Snapshot();
        Assert.Equal(["111", "222", "333"], after.History.Select(r => r.InstanceId));

        // 対象外へは移っていないことになるので、帯は消える。
        Assert.All(after.History, r => Assert.False(r.ExcludedBefore));

        // 既に数えた訪問の回数はそのまま。読み直しても二重に数えない。
        Assert.Equal(1, after.History[0].VisitOrdinal);
        Assert.Equal(1, after.History[2].VisitOrdinal);

        // 記録していなかった Friends の訪問も、回数だけは数えてあるので「回数不明」にならない（→実装メモ5.58）。
        Assert.Equal(1, after.History[1].VisitOrdinal);

        // 現在地（333）の印と、退出時刻（111 は 222 へ移った時刻）が付け直される。
        Assert.Equal(after.History[2].EventId, after.CurrentEventId);
        Assert.NotNull(after.History[0].LeftAtUtc);
        Assert.Equal(LogHealth.Ok, after.Health);
    }

    [Fact]
    public void 記録していない種類へ2回入ってから記録すると_2回目は2回目と数える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.Friends("222"), "B")
            + LogText.Move(SessionStart.AddMinutes(5), Loc.Public("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(10), Loc.Friends("222"), "B"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(12), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.Equal(["111"], harness.Snapshot().History.Select(r => r.InstanceId));

        harness.Engine.SetTargetTypes(new HashSet<AccessType>(TargetAccessTypes.Default) { AccessType.Friends });

        var history = harness.Snapshot().History;
        Assert.Equal(["222", "111", "222"], history.Select(r => r.InstanceId));
        Assert.Equal([1, 1, 2], history.Select(r => r.VisitOrdinal));
    }

    [Fact]
    public void 記録する種類から外した種類の行は消え_戻すと元の回数で戻る()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(5), Loc.Public("222"), "B"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(8), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.Equal(2, harness.Snapshot().History.Count);

        harness.Engine.SetTargetTypes(new HashSet<AccessType> { AccessType.Public });
        var publicOnly = harness.Snapshot().History;
        Assert.Equal(["222"], publicOnly.Select(r => r.InstanceId));

        // Group Public を外した今は、222 の前に対象外への移動があったことになる。
        // ただし先頭の行なので帯は出ない（→5.38）。
        Assert.True(publicOnly[0].ExcludedBefore);

        harness.Engine.SetTargetTypes(TargetAccessTypes.Default);
        var restored = harness.Snapshot().History;
        Assert.Equal(["111", "222"], restored.Select(r => r.InstanceId));
        Assert.All(restored, r => Assert.Equal(1, r.VisitOrdinal));
    }

    [Fact]
    public void 起動時に選んだ種類は最初から記録して数える()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, PublicFriendsPublic());

        using var harness = new EngineHarness(
            dir.Path,
            SessionStart.AddMinutes(12),
            Process(SessionStart),
            configure: o => o with { TargetTypes = new HashSet<AccessType> { AccessType.Public, AccessType.Friends } });

        harness.Engine.Initialize();

        var history = harness.Snapshot().History;
        Assert.Equal(["111", "222", "333"], history.Select(r => r.InstanceId));
        Assert.All(history, r => Assert.Equal(1, r.VisitOrdinal));
    }

    [Fact]
    public void 記録する種類を変えても目印とカウント延長は引き継ぐ()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        var file = dir.WriteSession(SessionStart, PublicFriendsPublic());

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(12), Process(SessionStart));
        harness.Engine.Initialize();

        var first = harness.Snapshot().History[0];
        harness.Engine.SetMark(first.EventId, InstanceMark.Heart);

        var leftAt = SessionStart.AddMinutes(13);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));
        var pressedAt = leftAt.AddMinutes(50);
        harness.SetNow(pressedAt);
        harness.Engine.Update();
        harness.Engine.ResetRetention();

        harness.Engine.SetTargetTypes(new HashSet<AccessType>(TargetAccessTypes.Default) { AccessType.Friends });

        var after = harness.Snapshot();
        Assert.Equal(InstanceMark.Heart, after.Marks[first.LocationKey]);
        Assert.Equal(harness.Utc(pressedAt.AddMinutes(60)), after.RetentionDeadlineUtc);
    }

    // ------------------------------------------------------------------ 設定ファイル

    [Fact]
    public void 保持時間と種類の既定は60分と4種類()
    {
        var settings = new AppSettings();

        Assert.Equal(60, settings.RetentionMinutes);
        Assert.Equal(["Public", "GroupPublic", "GroupPlus", "GroupOnly"], settings.TargetAccessTypes);
        Assert.True(settings.TargetTypes.SetEquals(TargetAccessTypes.Default));
        Assert.True(settings.ShowDesktopWindow);
        Assert.False(settings.DesktopWindowTopMost);
    }

    [Fact]
    public void 無効な保持時間と種類は知らせて既定値へ戻す()
    {
        var log = new CollectingDiagnostics();
        var settings = new AppSettings
        {
            RetentionMinutes = 0,
            TargetAccessTypes = ["friends", "Nope"],
        };

        settings.Validate(log);

        Assert.Equal(60, settings.RetentionMinutes);
        Assert.Contains(log.Messages, m => m.Contains("retentionMinutes"));
        Assert.Contains(log.Messages, m => m.Contains("Nope"));

        // 大文字小文字は問わず、知らない名前だけを落とす。綴りは列挙子の名前へそろえる。
        Assert.Equal(["Friends"], settings.TargetAccessTypes);

        var empty = new AppSettings { TargetAccessTypes = ["Unknown"] };
        empty.Validate(log);
        Assert.Equal(["Public", "GroupPublic", "GroupPlus", "GroupOnly"], empty.TargetAccessTypes);
    }

    [Fact]
    public void ウィンドウで変えた項目だけを書き戻し_他の記述は残す()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        File.WriteAllText(path, """
            {
              "// メモ": "利用者が書いた説明",
              "idFontPixels": 50,
              "RetentionMinutes": 60
            }
            """);

        var settings = AppSettings.Load(path, new CollectingDiagnostics());
        new DesktopSettings(
            RetentionMinutes: 90,
            TargetTypes: new HashSet<AccessType> { AccessType.Public, AccessType.Friends },
            PanelWidthMeters: 0.2f,
            BackgroundOpacity: 0.5f,
            ViewAngleLimitDegrees: 40f,
            ViewAngleFadeSeconds: 0.5f,
            ScrollRowsPerSecond: 8f,
            TopMost: true).ApplyTo(settings, SettingsField.RetentionMinutes | SettingsField.TargetAccessTypes);

        // 送られなかった項目（幅など）は変えない。
        Assert.Equal(0.14f, settings.OverlayWidthMeters);

        settings.SaveFields(path, SettingsField.RetentionMinutes | SettingsField.TargetAccessTypes);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("利用者が書いた説明", (string?)root["// メモ"]);
        Assert.Equal(50, (int?)root["idFontPixels"]);

        // 既にあるキーは元の綴りのまま置き換える。
        Assert.Equal(90, (int?)root["RetentionMinutes"]);
        Assert.False(root.ContainsKey("retentionMinutes"));
        Assert.Equal(["Public", "Friends"], root["targetAccessTypes"]!.AsArray().Select(n => (string?)n));
        Assert.False(root.ContainsKey("overlayWidthMeters"));

        var reloaded = AppSettings.Load(path, new CollectingDiagnostics());
        Assert.Equal(90, reloaded.RetentionMinutes);
        Assert.True(reloaded.TargetTypes.SetEquals([AccessType.Public, AccessType.Friends]));
    }

    [Fact]
    public void 設定の変更は落ち着いてからエンジンへ渡して保存する()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        var settings = new AppSettings();
        var changes = new SettingsChanges(settings, path, new CollectingDiagnostics(), harness.Clock);
        var values = DesktopSettings.From(settings);

        // −を押し続けて 55 → 50 → 45 と通り過ぎても、エンジンにはまだ渡らない。
        foreach (var minutes in new[] { 55, 50, 45 })
        {
            changes.Apply(values with { RetentionMinutes = minutes }, SettingsField.RetentionMinutes, runtime: null);
            harness.Clock.Advance(TimeSpan.FromSeconds(0.3));
            changes.Flush(harness.Engine, force: false);
        }

        Assert.Equal(TimeSpan.FromMinutes(60), harness.Engine.Retention);
        Assert.False(File.Exists(path));

        harness.Clock.Advance(SettingsChanges.SettleDelay);
        changes.Flush(harness.Engine, force: false);

        Assert.Equal(TimeSpan.FromMinutes(45), harness.Engine.Retention);
        Assert.Equal(SettingsField.None, changes.Pending);
        Assert.Equal(45, AppSettings.Load(path, new CollectingDiagnostics()).RetentionMinutes);
    }

    // ------------------------------------------------------------------ ウィンドウの中身

    private sealed class ViewFixture : IDisposable
    {
        public ViewFixture(AppSettings? settings = null)
        {
            View = new DesktopView(new PanelStyle(), DesktopSettings.From(settings ?? new AppSettings()), Commands.Add);
            View.Resize(DesktopView.DefaultClientSize, 1f);
            var rows = SampleRows.Build();
            View.SetRows(rows);
            RowCount = rows.Count;
            View.SetCountdown("59:12");
            View.SetStatus(DesktopStatus.Initial);
        }

        public DesktopView View { get; }

        public List<DesktopCommand> Commands { get; } = [];

        public void Click(PointF point)
        {
            View.MouseMove(point);
            View.MouseDown(point);
            View.MouseUp();
        }

        public void Click(RectangleF? rect)
        {
            Assert.NotNull(rect);
            Click(Center(rect.Value));
        }

        public void RightClick(PointF point)
        {
            View.MouseMove(point);
            View.RightMouseDown(point);
        }

        public static PointF Center(RectangleF rect) => new(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));

        /// <summary>最後の行（いま滞在している行）の中ほど。</summary>
        public PointF LastRow()
        {
            var panel = View.PanelRect;
            var scale = panel.Width / new PanelStyle().Width;
            var row = View.RowScreenRect(RowCount - 1);
            return new PointF(panel.X + (400f * scale), row.Y + (row.Height / 2f));
        }

        /// <summary>いま並べている行の数。</summary>
        public int RowCount { get; set; }

        public void Dispose() => View.Dispose();
    }

    [Theory]
    [InlineData(60f, +1, 65f)]
    [InlineData(60f, -1, 55f)]
    [InlineData(42f, +1, 45f)]
    [InlineData(42f, -1, 40f)]
    [InlineData(180f, +1, 180f)]
    [InlineData(5f, -1, 5f)]
    public void 数値の部品は刻みの倍数へそろえて動かす(float value, int direction, float expected)
        => Assert.Equal(expected, SettingsSteppers.StepValue(value, 5f, 5f, 180f, direction));

    /// <summary>リセットまでの時間の刻み（2026-09-27のユーザー指定→実装メモ5.71）。</summary>
    [Theory]
    [InlineData(55f, +1, 60f)]
    [InlineData(60f, +1, 70f)]
    [InlineData(60f, -1, 55f)]
    [InlineData(110f, +1, 120f)]
    [InlineData(120f, +1, 150f)]
    [InlineData(120f, -1, 110f)]
    [InlineData(270f, +1, 300f)]
    [InlineData(300f, +1, 360f)]
    [InlineData(300f, -1, 270f)]
    [InlineData(840f, +1, 900f)]
    [InlineData(900f, +1, 900f)]
    [InlineData(5f, -1, 5f)]
    [InlineData(64f, +1, 70f)] // 設定ファイルの半端な値からも、次の刻みへ
    [InlineData(64f, -1, 60f)]
    public void リセットまでの時間は長いほど粗く刻む(float value, int direction, float expected)
        => Assert.Equal(expected, SettingsSteppers.NextRetention(value, direction));

    /// <summary>背景の不透明度は90%から2%刻み（2026-09-27のユーザー指定→実装メモ5.77）。下限は50%（2026-09-29→実装メモ5.89）。</summary>
    [Theory]
    [InlineData(0.85f, +1, 0.90f)]
    [InlineData(0.90f, +1, 0.92f)]
    [InlineData(0.90f, -1, 0.85f)]
    [InlineData(0.98f, +1, 1.00f)]
    [InlineData(1.00f, +1, 1.00f)]
    [InlineData(1.00f, -1, 0.98f)]
    [InlineData(0.50f, -1, 0.50f)]
    [InlineData(0.50f, +1, 0.55f)]
    [InlineData(0.15f, +1, 0.50f)] // 以前の下限（15%）が書いてあっても、押せば50%へ
    [InlineData(0.93f, -1, 0.92f)] // 設定ファイルの半端な値からも、次の刻みへ
    [InlineData(0.87f, +1, 0.90f)]
    public void 背景の不透明度は90パーセントから細かく刻む(float value, int direction, float expected)
        => Assert.Equal(expected, SettingsSteppers.NextOpacity(value, direction));

    [Fact]
    public void 小数の刻みでも誤差を残さない()
    {
        Assert.Equal(0.145f, SettingsSteppers.StepValue(0.14f, 0.005f, 0.06f, 0.3f, +1));
        Assert.Equal(0.25f, SettingsSteppers.StepValue(0.2f, 0.05f, 0f, 2f, +1));
    }

    [Fact]
    public void ログリセットまでの時間のプラスで70分を送る()
    {
        using var f = new ViewFixture();

        f.Click(f.View.TargetRect(SettingsView.HitKind.StepperPlus, 0));

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(f.Commands));
        Assert.Equal(70, change.Settings.RetentionMinutes);
        Assert.Equal(SettingsField.RetentionMinutes, change.Fields);
        Assert.Equal("70 分", f.View.StepperText(0));
    }

    [Fact]
    public void 種類のチェックで付け外しし_最後の1つは外せない()
    {
        using var f = new ViewFixture(new AppSettings { TargetAccessTypes = ["Public"] });

        var friends = TargetAccessTypes.Selectable.ToList().IndexOf(AccessType.Friends);
        f.Click(f.View.TargetRect(SettingsView.HitKind.TargetType, friends));

        var added = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(f.Commands));
        Assert.True(added.Settings.TargetTypes.SetEquals([AccessType.Public, AccessType.Friends]));
        Assert.Equal(SettingsField.TargetAccessTypes, added.Fields);

        f.Click(f.View.TargetRect(SettingsView.HitKind.TargetType, friends));
        f.Commands.Clear();

        // Public だけが残った状態で Public を外そうとしても、何も送らない。
        f.Click(f.View.TargetRect(SettingsView.HitKind.TargetType, 0));
        Assert.Empty(f.Commands);
        Assert.True(f.View.Settings.TargetTypes.SetEquals([AccessType.Public]));
    }

    [Fact]
    public void 行を右クリックすると目印の選択肢が出て_選ぶとその行へ付ける()
    {
        using var f = new ViewFixture();

        // 右クリックがVR内のトリガーにあたる（左クリックは行を選ぶ→実装メモ5.42）。
        f.RightClick(f.LastRow());
        Assert.True(f.View.MarkPopupOpen);
        Assert.Empty(f.Commands);

        f.Click(f.View.MarkChoiceRect(1));

        var mark = Assert.IsType<DesktopCommand.SetMark>(Assert.Single(f.Commands));
        Assert.Equal(InstanceMarks.Choices[1], mark.Mark);
        Assert.False(f.View.MarkPopupOpen);

        // 選んだのは見えている最後の行（滞在中の行）。
        var rows = SampleRows.Build();
        Assert.Equal(rows[^1].EventId, mark.EventId);
    }

    [Fact]
    public void 選択肢の外をクリックすると何も変えずに閉じる()
    {
        using var f = new ViewFixture();

        f.RightClick(f.LastRow());
        Assert.True(f.View.MarkPopupOpen);

        // 設定の部品の上でも、まずポップアップが受け取って閉じるだけ。
        f.Click(f.View.TargetRect(SettingsView.HitKind.StepperPlus, 0));

        Assert.False(f.View.MarkPopupOpen);
        Assert.Empty(f.Commands);
    }

    [Fact]
    public void 見出しのカウント延長をクリックすると延長を送る()
    {
        using var f = new ViewFixture();

        f.Click(f.View.ResetButtonScreenRect());

        Assert.IsType<DesktopCommand.ExtendRetention>(Assert.Single(f.Commands));
        Assert.False(f.View.MarkPopupOpen);
    }

    [Fact]
    public void パネルの上のホイールでスクロールする()
    {
        using var f = new ViewFixture();
        var tail = f.View.ScrollOffset;
        var center = ViewFixture.Center(f.View.PanelRect);

        Assert.True(tail > 0f);

        // 上へ回すと古い側へ戻る。1ノッチで標準行の半分。
        f.View.MouseWheel(center, 120);
        Assert.Equal(tail - (new PanelStyle().RowHeight / 2f), f.View.ScrollOffset, 2);

        f.View.MouseWheel(center, -120);
        Assert.Equal(tail, f.View.ScrollOffset, 2);
    }

    [Fact]
    public void 数値の部品の上のホイールで値を動かす()
    {
        using var f = new ViewFixture();

        // 背景の不透明度は「VRオーバーレイ設定」のタブにある（→実装メモ5.42）。
        // 既定の大きさでは送れるので（予告通知を足した→実装メモ5.89）、送れる間はホイールで送ってしまう。入り切る高さにして確かめる。
        f.View.Resize(new Size(1060, 1400), 1f);
        f.View.SelectTab(DesktopTab.Panel);

        f.View.MouseWheel(ViewFixture.Center(f.View.TargetRect(SettingsView.HitKind.StepperMinus, 2)!.Value), -120);

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(f.Commands));
        Assert.Equal(0.85f, change.Settings.BackgroundOpacity, 3);
        Assert.Equal(SettingsField.BackgroundOpacity, change.Fields);
    }

    [Fact]
    public void 見出しのリセットは確認を挟んでから履歴を消す()
    {
        using var f = new ViewFixture();

        // 押しただけでは消さず、確認を出す（→実装メモ5.65）。
        f.Click(f.View.ClearButtonScreenRect());
        Assert.True(f.View.ClearConfirmOpen);
        Assert.Empty(f.Commands);

        f.Click(f.View.ConfirmButtonRect(PanelGeometry.ConfirmAccept));
        Assert.IsType<DesktopCommand.ClearHistory>(Assert.Single(f.Commands));
        Assert.False(f.View.ClearConfirmOpen);
    }

    [Fact]
    public void 確認のキャンセルと枠の外では何も消さない()
    {
        using var f = new ViewFixture();

        f.Click(f.View.ClearButtonScreenRect());
        f.Click(f.View.ConfirmButtonRect(PanelGeometry.ConfirmCancel));
        Assert.False(f.View.ClearConfirmOpen);

        // 枠の外（行の上）を押しても閉じるだけで、行も選ばない。
        f.Click(f.View.ClearButtonScreenRect());
        f.Click(f.LastRow());
        Assert.False(f.View.ClearConfirmOpen);
        Assert.Null(f.View.SelectedEventId);

        // 右クリックでも閉じる。
        f.Click(f.View.ClearButtonScreenRect());
        f.RightClick(f.LastRow());
        Assert.False(f.View.ClearConfirmOpen);
        Assert.False(f.View.MarkPopupOpen);

        Assert.Empty(f.Commands);
    }

    [Fact]
    public void リセットまでの時間の増減ボタンは値に寄せた21_6px()
    {
        using var f = new ViewFixture();

        // 30px から 0.9倍（→実装メモ5.68）、さらに 0.8倍（→5.69）。値の幅も詰めて −／＋ を値へ寄せた（間は84px）。
        var minus = f.View.TargetRect(SettingsView.HitKind.StepperMinus, 0)!.Value;
        var plus = f.View.TargetRect(SettingsView.HitKind.StepperPlus, 0)!.Value;
        Assert.Equal(21.6f, plus.Width, 1);
        Assert.Equal(21.6f, plus.Height, 1);
        Assert.Equal(84f, plus.X - minus.Right, 1);
    }

    [Fact]
    public void 既定の大きさでは2つの設定のタブを送って一番下まで出せる()
    {
        using var f = new ViewFixture();

        // 一般設定は「インスタンス操作」「グループ名」を移して長くなったので送る（→実装メモ5.71）。
        // VRオーバーレイ設定も「履歴自動リセットの予告通知」を足して入り切らなくなったので送る（→実装メモ5.89）。
        foreach (var (tab, last) in new[] { (DesktopTab.Panel, SettingsView.HitKind.WarningReshow), (DesktopTab.Startup, SettingsView.HitKind.CopyCommand) })
        {
            f.View.SelectTab(tab);
            Assert.True(f.View.SettingsScrollMax > 0f);

            var tabs = f.View.TabRect(tab);
            var besideControls = new PointF(tabs.X + 4f, tabs.Bottom + 14f);

            for (var i = 0; i < 80; i++)
                f.View.MouseWheel(besideControls, -120);

            Assert.Equal(f.View.SettingsScrollMax, f.View.SettingsScroll, 1);
            Assert.True(f.View.TargetRect(last)!.Value.Bottom <= DesktopView.DefaultClientSize.Height);
        }

        Assert.Empty(f.Commands);
    }

    [Fact]
    public void 行が収まりスクロールの溝がないときは選んだ行の枠をパネルの右端まで伸ばす()
    {
        using var f = new ViewFixture();
        f.View.SetRows(SampleRows.Build().TakeLast(2).ToList());
        f.RowCount = 2;

        f.Click(f.LastRow());
        Assert.NotNull(f.View.SelectedEventId);

        // 枠の右辺（アクセント色）は、溝の手前ではなくパネルの右端に来る（→実装メモ5.67）。
        using var bitmap = f.View.RenderToBitmap();
        var panel = f.View.PanelRect;
        var y = (int)f.LastRow().Y;
        var edge = bitmap.GetPixel((int)MathF.Floor(panel.Right) - 1, y);
        var accent = new PanelStyle().Accent;

        Assert.True(Math.Abs(edge.G - accent.G) < 40 && Math.Abs(edge.B - accent.B) < 40, $"{edge}");
    }

    [Fact]
    public void SteamVRにつながっていない間の自動起動は指すと理由を出す()
    {
        using var f = new ViewFixture();
        f.View.Resize(new Size(1060, 1400), 1f); // 「起動」は一般設定の下のほう（→実装メモ5.72）
        f.View.SetStartupState(null, false);

        var rect = f.View.TargetRect(SettingsView.HitKind.LaunchWithSteamVr)!.Value;
        f.View.MouseMove(ViewFixture.Center(rect));
        Assert.Equal("SteamVR を起動中のみ変更できます", f.View.SettingsHint);

        // 押しても変えない。
        f.Click(rect);
        Assert.Empty(f.Commands);

        f.View.MouseLeave();
        Assert.Null(f.View.SettingsHint);

        // つながっていれば出さない。
        f.View.SetStartupState(false, false);
        f.View.MouseMove(ViewFixture.Center(rect));
        Assert.Null(f.View.SettingsHint);
    }

    [Fact]
    public void 延長とリセットは見出しの右端に並び重ならない()
    {
        using var f = new ViewFixture();

        var extend = f.View.ResetButtonScreenRect();
        var clear = f.View.ClearButtonScreenRect();

        Assert.True(extend.Right < clear.X);
        Assert.Equal(extend.Y + (extend.Height / 2f), clear.Y + (clear.Height / 2f), 1);

        f.Click(extend);
        Assert.IsType<DesktopCommand.ExtendRetention>(Assert.Single(f.Commands));
        Assert.False(f.View.ClearConfirmOpen);
    }

    [Fact]
    public void 表示倍率150パーセントでも右の設定が最小の大きさに収まる()
    {
        using var f = new ViewFixture();
        var scale = 1.5f;

        f.View.Resize(new Size((int)(DesktopView.MinClientSize.Width * scale), (int)(DesktopView.MinClientSize.Height * scale)), scale);

        // 入り切らないタブは、ホイールで送って一番下の部品まで出せる（→実装メモ5.42・5.67・5.71）。
        // VRオーバーレイ設定の一番下は、予告通知の「AFKから復帰時に再表示する」（→実装メモ5.89）。
        foreach (var (tab, last) in new[] { (DesktopTab.Panel, SettingsView.HitKind.WarningReshow), (DesktopTab.Startup, SettingsView.HitKind.CopyCommand) })
        {
            f.View.SelectTab(tab);

            // 部品のない所（タブのすぐ下＝まとまりの見出しの帯）でホイールを回す。
            var tabs = f.View.TabRect(tab);
            var besideControls = new PointF(tabs.X + 4f, tabs.Bottom + (14f * scale));

            for (var i = 0; i < 80; i++)
                f.View.MouseWheel(besideControls, -120);

            Assert.Equal(f.View.SettingsScrollMax, f.View.SettingsScroll, 1);
            var lastControl = f.View.TargetRect(last)!.Value;
            Assert.True(lastControl.Bottom <= DesktopView.MinClientSize.Height * scale);
        }

        Assert.Empty(f.Commands); // 送るだけで設定は変えない

        f.View.SelectTab(DesktopTab.Startup);

        // パネルは等倍（論理px）より大きくしない。左の欄に収まる。
        var panel = f.View.PanelRect;
        Assert.True(panel.Width <= new PanelStyle().Width * scale + 0.5f);
        Assert.True(panel.Right <= f.View.TargetRect(SettingsView.HitKind.StepperMinus, 0)!.Value.X);

        using var bitmap = f.View.RenderToBitmap();
        Assert.Equal((int)(DesktopView.MinClientSize.Width * scale), bitmap.Width);
    }

    [Fact]
    public void ウィンドウのパネルは手首のパネルと同じ絵を縮小したもの()
    {
        using var f = new ViewFixture();
        using var bitmap = f.View.RenderToBitmap();

        // 手首のパネルの見出しの色（Header）がパネルの左上に出ている（背景の不透明度を窓の地色へ重ねた色）。
        var panel = f.View.PanelRect;
        var pixel = bitmap.GetPixel((int)panel.X + 4, (int)panel.Y + 4);
        var style = new PanelStyle();
        var alpha = style.BackgroundOpacity;

        Assert.InRange(pixel.R, (int)((style.Header.R * alpha) - 2), (int)(style.Header.R + 2));
        Assert.InRange(pixel.G, (int)((style.Header.G * alpha) - 2), (int)(style.Header.G + 2));
        Assert.InRange(pixel.B, (int)((style.Header.B * alpha) - 2), (int)(style.Header.B + 2));
    }

    [Fact]
    public void 手首パネルの状態を言葉にする()
    {
        static string Wrist(DesktopStatus status) => StatusText.Build(status)[3].Value;

        Assert.Equal("SteamVR未接続", Wrist(DesktopStatus.Initial));

        var connected = DesktopStatus.Initial with { VrConnected = true, ClientRunning = true, Health = LogHealth.Ok };
        Assert.Equal("非表示・メインメニュー非表示", Wrist(connected));
        Assert.Equal("表示中", Wrist(connected with { VrHideReason = PanelHideReason.None }));
        Assert.Equal("非表示・基準角度未満", Wrist(connected with { VrHideReason = PanelHideReason.ViewAngle }));
        Assert.Equal("非表示・VRChat未起動", Wrist(connected with { ClientRunning = false }));
    }
}
