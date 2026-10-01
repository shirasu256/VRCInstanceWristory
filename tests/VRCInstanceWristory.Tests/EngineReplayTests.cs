using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 仕様11.1節の提供ログを再生した結果（受入項目 L01〜L06）。
/// 回数は仕様11.1節の明示的な0開始条件で評価する。
///
/// 読むのは提供ログから作った合成ログである（2026-10-01）。エンジンが読む行だけを時刻ごと残し、
/// 人・ワールド・グループのIDと名前、写真の保存先を架空のものへ置き換えてある。
/// 元の提供ログは公開しないので、リポジトリには置かない。
/// </summary>
public class EngineReplayTests
{
    private const string LogFileName = "output_log_2026-09-11_00-48-17.txt";

    private static readonly DateTime SessionStart = new(2026, 9, 11, 0, 48, 17);

    /// <summary>
    /// 提供ログだけを置いたフォルダー。リポジトリの <c>tests\VRCInstanceWristory.Tests\Fixtures</c> を、
    /// ビルドで検証の実行フォルダーへ写したもの（ほかのログが混ざらないので、そのままログフォルダーとして読める）。
    /// </summary>
    private static string LogDirectory()
    {
        var directory = TestPaths.Fixtures;

        if (!File.Exists(Path.Combine(directory, LogFileName)))
            throw new FileNotFoundException($"提供ログ {LogFileName} が {directory} にありません。");

        return directory;
    }

    private static EngineHarness Replay(DateTime? untilLocal)
    {
        var harness = new EngineHarness(
            LogDirectory(),
            SessionStart,
            new ClientProcessInfo(-1, Utc(SessionStart)),
            configure: options => options with
            {
                OnlyFileName = LogFileName,
                AssumeEpochStart = true,
                SourceNamespace = "r",
                StopAtUtc = untilLocal is null ? null : Utc(untilLocal.Value),
            },
            followLogTime: true);

        harness.Engine.Initialize();

        if (untilLocal is { } stop)
            harness.SetNow(stop);

        return harness;
    }

    [Fact]
    public void L01_対象の成功を9件抽出する()
    {
        using var harness = Replay(untilLocal: null);

        Assert.Equal(9, harness.Visits.Count);
        Assert.Equal(3, harness.Visits.Count(v => v.AccessType == AccessType.Public));
        Assert.Equal(6, harness.Visits.Count(v => v.AccessType == AccessType.GroupPublic));
        Assert.Equal(6, harness.Visits.Select(v => v.LocationKey).Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(
            ["07254", "29719", "07254", "39437", "24688", "c1e88b6419", "db7da28295", "07254", "07254"],
            harness.Visits.Select(v => v.InstanceId));

        // ワールド名とGroup IDも対応づく。
        Assert.Equal("テストワールド05", harness.Visits[0].WorldName);
        Assert.Equal("g:00000001", harness.Visits[0].ShortGroupId);
        Assert.Equal("テストワールド06", harness.Visits[4].WorldName);
        Assert.Null(harness.Visits[4].GroupId);
    }

    [Fact]
    public void L02_01時12分までの表示は7行で回数は1_1_2_1_1_1_1()
    {
        using var harness = Replay(new DateTime(2026, 9, 11, 1, 12, 0));
        var snapshot = harness.Snapshot();

        Assert.Equal(
            ["07254", "29719", "07254", "39437", "24688", "c1e88b6419", "db7da28295"],
            snapshot.History.Select(v => v.InstanceId));

        Assert.Equal([1, 1, 2, 1, 1, 1, 1], snapshot.History.Select(v => v.VisitOrdinal));
        Assert.Equal(PresenceState.InTarget, snapshot.Presence);

        // 2026-09-13の変更で、表示条件はワールドタブの開閉になった。
        // 01:00:56 に開いたあと、2026-09-21の変更で退出では閉じなくなり、
        // 閉じるのは B / Y か「移動後に手首の角度で隠れたとき」だけになった。
        // 再生にはVRの手首がないため、この時刻でも開いたままになる。
        Assert.True(snapshot.MenuPageOpen);
        Assert.True(snapshot.AwaitingViewAngleClose);
        Assert.True(snapshot.ContentReady);
    }

    [Fact]
    public void L03_満員エラーでは記録せず現在地を維持する()
    {
        using var harness = Replay(new DateTime(2026, 9, 11, 1, 11, 30));
        var snapshot = harness.Snapshot();

        Assert.DoesNotContain(harness.Visits, v => v.InstanceId == "28006");
        Assert.Equal(PresenceState.InTarget, snapshot.Presence);

        var current = snapshot.History.Single(v => v.EventId == snapshot.CurrentEventId);
        Assert.Equal("c1e88b6419", current.InstanceId);
    }

    [Fact]
    public void L04_01時16分15秒までで9行になり07254は1から4回目になる()
    {
        using var harness = Replay(new DateTime(2026, 9, 11, 1, 16, 15));
        var snapshot = harness.Snapshot();

        Assert.Equal(9, snapshot.History.Count);

        var repeats = snapshot.History.Where(v => v.InstanceId == "07254").ToList();
        Assert.Equal(4, repeats.Count);
        Assert.Equal([1, 2, 3, 4], repeats.Select(v => v.VisitOrdinal));

        // 現在の入室行だけに印が付く（過去の同じIDの行は現在地扱いしない）。
        Assert.Equal(snapshot.History[^1].EventId, snapshot.CurrentEventId);
    }

    [Fact]
    public void ワールドタブを開いている時刻ではパネルを出す()
    {
        // 提供ログの 01:00:56 に OnPageAboutToShow、01:01:01 に OnPageHidden がある。
        using var harness = Replay(new DateTime(2026, 9, 11, 1, 0, 58));
        var snapshot = harness.Snapshot();

        Assert.True(snapshot.MenuPageOpen);
        Assert.True(snapshot.ContentReady);
        Assert.Single(snapshot.History);
        Assert.Equal("07254", snapshot.History[0].InstanceId);

        // 現在地が対象なら、その行に印が付く。
        Assert.Equal(snapshot.History[0].EventId, snapshot.CurrentEventId);
    }

    [Fact]
    public void 提供ログでもページを隠すログでは閉じない()
    {
        // 01:01:01 に OnPageAboutToHide / OnPageHidden があるが、閉じる判定には使わない。
        using var harness = Replay(new DateTime(2026, 9, 11, 1, 1, 3));
        var snapshot = harness.Snapshot();

        Assert.True(snapshot.MenuPageOpen);
        Assert.True(snapshot.ContentReady);
    }

    [Fact]
    public void 提供ログでもインスタンスから退出しても閉じない()
    {
        // 2026-09-21のユーザー指定。01:01:06 の OnLeftRoom では閉じず、
        // ロード画面の間も出したままにする（2026-09-22の指定でメインメニューと同じ扱い）。
        using var harness = Replay(new DateTime(2026, 9, 11, 1, 1, 8));
        var snapshot = harness.Snapshot();

        Assert.True(snapshot.InLoadingScreen);
        Assert.True(snapshot.MenuPageOpen);
        Assert.True(snapshot.ContentReady);
        Assert.Single(snapshot.History);
    }

    [Fact]
    public void 入室の確定ではなくロード完了でロード画面を抜ける()
    {
        // 提供ログでは 01:01:13 に Successfully joined room、01:01:21 に
        // Finished entering world. がある。ロード画面の終わりは後者で判定する
        // （2026-09-22のユーザー指定）。
        using var joined = Replay(new DateTime(2026, 9, 11, 1, 1, 15));

        Assert.Equal(PresenceState.InTarget, joined.Snapshot().Presence); // 入室は確定している
        Assert.True(joined.Snapshot().InLoadingScreen);                   // それでもまだロード画面

        using var harness = Replay(new DateTime(2026, 9, 11, 1, 1, 25));
        var snapshot = harness.Snapshot();

        Assert.False(snapshot.InLoadingScreen);
        Assert.True(snapshot.MenuPageOpen);
        Assert.True(snapshot.AwaitingViewAngleClose);
        Assert.True(snapshot.ContentReady);

        // VR側が角度で隠したときに呼ばれる経路。ここで初めて閉じる。
        harness.Engine.CloseByViewAngle();

        var closed = harness.Snapshot();
        Assert.False(closed.MenuPageOpen);
        Assert.False(closed.AwaitingViewAngleClose);
        Assert.False(closed.ContentReady);
        Assert.Equal(2, closed.History.Count); // 履歴は保つ
    }

    [Fact]
    public void L05_Friends入室でも履歴は保ち現在地の印だけ外す()
    {
        // 仕様のL05は「Friends入室では非表示」だったが、2026-09-13の変更で
        // 表示条件はメインメニューの開閉になった。滞在先が対象外でも、
        // 開いていれば出す（履歴は保ち、現在地の印だけ付けない）。
        using var harness = Replay(new DateTime(2026, 9, 11, 1, 22, 30));
        var snapshot = harness.Snapshot();

        Assert.Equal(9, snapshot.History.Count);
        Assert.Equal(PresenceState.InExcluded, snapshot.Presence);
        Assert.Null(snapshot.CurrentEventId);

        // 再生にはVRの手首がないため、移動後の角度による自動的な非表示は起きない。
        Assert.True(snapshot.MenuPageOpen);
        Assert.True(snapshot.ContentReady);
    }

    [Fact]
    public void L06_末尾では終了扱いになり履歴はすべて期限切れになる()
    {
        using var harness = Replay(untilLocal: null);
        harness.SetNow(new DateTime(2026, 9, 11, 13, 17, 53));

        var snapshot = harness.Snapshot();

        Assert.Equal(PresenceState.Ended, snapshot.Presence);
        Assert.Empty(snapshot.History);
        Assert.False(snapshot.ContentReady);

        // 行に付けた回数は残る（明示的な0開始条件での再生）。回数の台帳は、自動リセットで数え直している（→実装メモ5.110）。
        Assert.Equal(4, harness.Visits.Max(v => v.VisitOrdinal));
        Assert.True(harness.Engine.Counter.Counts.Values.Max() < 4);
        Assert.Equal(6, harness.Visits.Select(v => v.LocationKey).Distinct().Count());
    }

    [Fact]
    public void 提供ログの先頭のInviteは対象外として記録しない()
    {
        using var harness = Replay(new DateTime(2026, 9, 11, 0, 50, 0));
        var snapshot = harness.Snapshot();

        Assert.Empty(harness.Visits);
        Assert.Equal(PresenceState.InExcluded, snapshot.Presence);
        Assert.False(snapshot.ContentReady);
    }
}
