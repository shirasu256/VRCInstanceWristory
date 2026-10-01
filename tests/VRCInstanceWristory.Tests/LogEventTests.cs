using VRCInstanceWristory.Core.Logging;

namespace VRCInstanceWristory.Tests;

/// <summary>仕様6.1節（イベントの判定）。接頭辞まで一致を検査し、埋め込み文字列では判定しない。</summary>
public class LogEventTests
{
    private static LogEvent Parse(string line)
        => LogEventParser.Parse(LogLineParser.Parse(line, 0, 1));

    [Fact]
    public void 実ログの4行を解析する()
    {
        var entering = LogLineParser.Parse("2026.09.11 00:57:08 Debug      -  [Behaviour] Entering Room: ～まったり交流ラウンジ～［OpenBeta］", 0, 1);
        Assert.Equal(LogLineKind.Entry, entering.Kind);
        Assert.Equal(new DateTime(2026, 9, 11, 0, 57, 8), entering.TimestampLocal);
        Assert.Equal("Debug", entering.Level);

        var ev = LogEventParser.Parse(entering);
        Assert.Equal(LogEventKind.EnteringRoom, ev.Kind);
        Assert.Equal("～まったり交流ラウンジ～［OpenBeta］", ev.Payload);

        var joining = Parse("2026.09.11 00:57:08 Debug      -  [Behaviour] Joining wrld_00000000-0000-4000-9000-000000000005:07254~group(grp_00000001-0000-4000-a000-000000000000)~groupAccessType(public)~region(jp)");
        Assert.Equal(LogEventKind.Joining, joining.Kind);
        Assert.StartsWith("wrld_00000000-0000-4000-9000-000000000005", joining.Payload);

        Assert.Equal(LogEventKind.JoiningOrCreatingRoom, Parse("2026.09.11 00:57:08 Debug      -  [Behaviour] Joining or Creating Room: ～まったり交流ラウンジ～［OpenBeta］").Kind);
        Assert.Equal(LogEventKind.JoinedRoom, Parse("2026.09.11 00:57:08 Debug      -  [Behaviour] Successfully joined room").Kind);
    }

    [Theory]
    [InlineData("2026.09.11 01:11:28 Error      -  [Behaviour] Could not enter room because: Instance is full", LogEventKind.JoinFailed)]
    [InlineData("2026.09.11 01:11:28 Error      -  Failed to join user in instance wrld_x:1 - full", LogEventKind.JoinFailed)]
    [InlineData("2026.09.11 13:17:53 Debug      -  VRCApplication: HandleApplicationQuit at 44974.6", LogEventKind.ApplicationQuit)]
    [InlineData("2026.09.11 00:48:29 Debug      -  [Behaviour] OnDisconnected: DisconnectByClientLogic", LogEventKind.Disconnected)]
    [InlineData("2026.09.11 00:51:36 Debug      -  [Behaviour] OnLeftRoom", LogEventKind.LeftRoom)]
    [InlineData("2026.09.20 00:52:18 Debug      -  [Behaviour] OnPlayerLeft テスト利用者⁄Tester (usr_00000000-0000-4000-8000-000000000001)", LogEventKind.PlayerLeft)]
    [InlineData("2026.09.11 00:48:28 Debug      -  [Behaviour] Destination set: wrld_x:1", LogEventKind.DestinationSet)]
    // パネルを出すきっかけにするメインメニューのページ（実ログで確認）
    [InlineData("2026.09.11 00:55:12 Warning    -  VP MainMenuWorlds OnPageAboutToShow()", LogEventKind.MainMenuPageShown)]
    [InlineData("2026.09.14 02:53:02 Warning    -  VP MainMenuLiveNow OnPageAboutToShow()", LogEventKind.MainMenuPageShown)]
    [InlineData("2026.09.12 00:34:41 Warning    -  VP MainMenuSocial OnPageAboutToShow()", LogEventKind.MainMenuPageShown)]
    [InlineData("2026.09.16 16:16:27 Warning    -  VP MainMenuVRChatPlusSubscriptions OnPageAboutToShow()", LogEventKind.MainMenuPageShown)]
    [InlineData("2026.09.11 00:57:08 Debug      -  [Behaviour] OnPlayerJoined テスト利用者⁄Tester (usr_00000000-0000-4000-8000-000000000001)", LogEventKind.PlayerJoined)]
    // ロード画面の終わり。末尾の句点が無くなっても拾えるよう先頭一致にしてある
    [InlineData("2026.09.11 01:01:21 Debug      -  [Behaviour] Finished entering world.", LogEventKind.FinishedEnteringWorld)]
    [InlineData("2026.09.11 01:01:21 Debug      -  [Behaviour] Finished entering world", LogEventKind.FinishedEnteringWorld)]
    // 正常終了は HandleApplicationQuit（上）と OnApplicationQuit のどちらでも終了として扱う
    // （2026-09-21のユーザー指定→5.30節）。
    [InlineData("2026.09.11 13:17:53 Debug      -  VRCApplication: OnApplicationQuit at 44974.6", LogEventKind.ApplicationQuit)]
    public void 接頭辞つきのイベントを判定する(string line, LogEventKind expected)
        => Assert.Equal(expected, Parse(line).Kind);

    [Theory]
    // 接頭辞のない同名文字列、埋め込み、別メッセージは訪問・現在地に使わない。
    [InlineData("2026.09.11 00:57:08 Debug      -  Successfully joined room")]
    [InlineData("2026.09.11 00:57:08 Debug      -  [Behaviour] Player joined: Successfully joined room")]
    [InlineData("2026.09.11 00:57:08 Debug      -  [Behaviour] OnDestroy")]
    [InlineData("2026.09.11 00:57:08 Debug      -  [Behaviour] showing disconnect reason")]
    [InlineData("2026.09.11 00:57:08 Debug      -  [Behaviour] Destination requested: wrld_x:1")]
    [InlineData("2026.09.11 00:57:08 Debug      -  [Behaviour] Destination fetching: wrld_x:1")]
    // 在室者の出入りは OnPlayerJoined / OnPlayerLeft だけで数える。似た名前の行は使わない。
    [InlineData("2026.09.11 00:57:08 Debug      -  [Behaviour] OnPlayerLeftRoom")]
    [InlineData("2026.09.11 00:57:08 Debug      -  [Behaviour] OnPlayerJoinedRoom")]
    [InlineData("2026.09.11 00:57:08 Debug      -  Removed player テスト利用者")]
    [InlineData("2026.09.11 00:57:08 Debug      -  [Behaviour] Rejoining local world: wrld_x:1")]
    // VP の接頭辞は行の先頭だけを見る。
    [InlineData("2026.09.11 00:55:12 Warning    -  [UI] VP MainMenuWorlds OnPageAboutToShow()")]
    public void 紛らわしい行は訪問に使わない(string line)
        => Assert.Equal(LogEventKind.Other, Parse(line).Kind);

    [Theory]
    // 同じページの他のイベントでは開かない。閉じたかの判定（→実装メモ5.104）の並びを見るためだけに返す。
    [InlineData("2026.09.11 00:55:12 Warning    -  VP MainMenuWorlds OnPageShown()", LogEventKind.MenuPageOther)]
    [InlineData("2026.09.11 00:55:15 Warning    -  VP MainMenuWorlds OnPageHidden()", LogEventKind.MenuPageOther)]
    [InlineData("2026.09.11 00:55:12 Warning    -  VP MainMenuWorlds OnPageAddedToStack()", LogEventKind.MenuPageOther)]
    [InlineData("2026.09.11 00:55:12 Warning    -  VP MainMenuWorlds OnNavigated() MainMenuWorlds", LogEventKind.MenuPageOther)]
    // 対象に挙げていないページには反応しない。
    [InlineData("2026.09.11 00:55:12 Warning    -  VP MainMenuSettings OnPageAboutToShow()", LogEventKind.MenuPageOther)]
    [InlineData("2026.09.11 00:55:12 Warning    -  VP MainMenuWorldsExtra OnPageAboutToShow()", LogEventKind.MenuPageOther)]
    [InlineData("2026.09.11 00:55:12 Warning    -  VP QuickMenuSocial OnPageAboutToShow()", LogEventKind.MenuPageOther)]
    // メインメニューを閉じたときの並びの2行。メインメニューのページなら対象外のページでも返す。
    [InlineData("2026.10.01 12:45:14 Warning    -  VP MainMenuWorlds OnWillCloseAllChildPages()", LogEventKind.MainMenuClosing)]
    [InlineData("2026.10.01 12:45:14 Warning    -  VP MainMenuWorlds OnPageAboutToHide()", LogEventKind.MainMenuPageHiding)]
    [InlineData("2026.10.01 12:45:14 Warning    -  VP MainMenuMarketplace OnWillCloseAllChildPages()", LogEventKind.MainMenuClosing)]
    // メインメニューのページでなければ、同じ呼び出しでも並びには使わない。
    [InlineData("2026.10.01 12:39:56 Warning    -  VP Popups_Root OnWillCloseAllChildPages()", LogEventKind.MenuPageOther)]
    [InlineData("2026.10.01 12:39:56 Warning    -  VP QuickMenuSelectedUser OnPageAboutToHide()", LogEventKind.MenuPageOther)]
    public void メニューのページの行を読み分ける(string line, LogEventKind expected)
        => Assert.Equal(expected, Parse(line).Kind);

    [Fact]
    public void 閉じる並びの行にページ名が入る()
    {
        Assert.Equal("MainMenuSocial", Parse("2026.10.01 12:46:07 Warning    -  VP MainMenuSocial OnWillCloseAllChildPages()").Payload);
        Assert.Equal("MainMenuSocial", Parse("2026.10.01 12:46:07 Warning    -  VP MainMenuSocial OnPageAboutToHide()").Payload);
    }

    [Fact]
    public void ページ名がイベントに入る()
    {
        var ev = Parse("2026.09.16 16:16:27 Warning    -  VP MainMenuVRChatPlusSubscriptions OnPageAboutToShow()");

        Assert.Equal(LogEventKind.MainMenuPageShown, ev.Kind);
        Assert.Equal("MainMenuVRChatPlusSubscriptions", ev.Payload);
    }

    [Fact]
    public void 継続行は日時なしとして扱う()
    {
        var line = LogLineParser.Parse("  at VRC.Udon.UdonBehaviour.ManagedUpdate () [0x00000]", 0, 1);

        Assert.Equal(LogLineKind.Continuation, line.Kind);
        Assert.False(line.HasValidTimestamp);
        Assert.Equal(LogEventKind.Other, LogEventParser.Parse(line).Kind);
    }

    [Fact]
    public void 日時が壊れた行は見出しとして扱わない()
    {
        var line = LogLineParser.Parse("2026.99.99 99:99:99 Debug      -  [Behaviour] Successfully joined room", 0, 1);

        Assert.Equal(LogLineKind.BadTimestamp, line.Kind);
        Assert.False(line.HasValidTimestamp);

        // イベントの種別は判定するが、日時が無効なので現在地は確定できない。
        Assert.Equal(LogEventKind.JoinedRoom, LogEventParser.Parse(line).Kind);
    }

    [Fact]
    public void 行末の空白だけを取り除く()
    {
        var line = LogLineParser.Parse("2026.09.11 00:57:08 Debug      -  [Behaviour] Entering Room:  空白つき   \r", 0, 1);

        Assert.Equal(LogLineKind.Entry, line.Kind);
        Assert.Equal("[Behaviour] Entering Room:  空白つき", line.Message);
    }
}
