namespace VRCInstanceWristory.Core.Logging;

/// <summary>
/// メッセージ本文を既知のイベントへ対応づける。仕様6.1節。
/// 接頭辞まで含めて先頭一致で判定し、別メッセージ内に埋め込まれた同名文字列では判定しない。
/// エラー説明文の全文一致にも依存しない。
/// </summary>
public static class LogEventParser
{
    private const string Behaviour = "[Behaviour] ";

    // 接頭辞つき（提供ログで確認済み）
    private const string EnteringRoomPrefix = Behaviour + "Entering Room: ";
    private const string JoiningPrefix = Behaviour + "Joining wrld_";
    private const string JoiningOrCreatingPrefix = Behaviour + "Joining or Creating Room: ";
    private const string JoinedRoomExact = Behaviour + "Successfully joined room";
    private const string DestinationSetPrefix = Behaviour + "Destination set: ";
    private const string LeftRoomExact = Behaviour + "OnLeftRoom";
    private const string PlayerJoinedPrefix = Behaviour + "OnPlayerJoined ";
    private const string PlayerLeftPrefix = Behaviour + "OnPlayerLeft ";
    private const string DisconnectedPrefix = Behaviour + "OnDisconnected: ";
    private const string CouldNotEnterPrefix = Behaviour + "Could not enter room because: ";

    // ロード画面の終わり。実ログでは "[Behaviour] Finished entering world." と末尾に句点が付く。
    // 版によって句点の有無が変わっても拾えるよう、先頭一致で判定する。
    private const string FinishedEnteringWorldPrefix = Behaviour + "Finished entering world";

    // 接頭辞なし（提供ログで確認済み）
    private const string FailedToJoinPrefix = "Failed to join user in instance ";

    // ログインしている利用者本人（提供ログで確認済み。接頭辞なし）。「一緒にいた人」から自分を除くのに使う。
    private const string UserAuthenticatedPrefix = "User Authenticated: ";

    // VR の方式（実ログで確認済み。"StartVRSDK: OpenVRLoader"。接頭辞なし→実装メモ5.85）。
    private const string VrSdkPrefix = "StartVRSDK: ";

    // 写真の保存（実ログで確認済み）。保存先は "H:/…/VRC-Photo\2026-09\VRChat_….png" のように区切りが混ざる。
    private const string ScreenshotPrefix = "[VRC Camera] Took screenshot to: ";

    // 正常終了の記録。実ログでは "VRCApplication: HandleApplicationQuit at 44974.6" のように出る。
    // 版によって OnApplicationQuit になるため、どちらも終了として扱う（2026-09-21のユーザー指定）。
    private static readonly string[] QuitPrefixes =
    [
        "VRCApplication: HandleApplicationQuit",
        "VRCApplication: OnApplicationQuit",
    ];

    // パネルを出すきっかけにするメインメニューのページ（実ログで確認。レベルは Warning、接頭辞なし）。
    // 増やす場合はここへ1行足す。
    private static readonly string[] MenuPages =
    [
        "MainMenuWorlds",
        "MainMenuLiveNow",
        "MainMenuSocial",
        "MainMenuVRChatPlusSubscriptions",
    ];

    private const string MenuPrefix = "VP ";
    private const string ShownCall = "OnPageAboutToShow";

    // メインメニューを閉じたときは、開いていたページについて次の2行が隣り合って出る（実ログで確認→実装メモ5.104）。
    //   VP MainMenuWorlds OnWillCloseAllChildPages()
    //   VP MainMenuWorlds OnPageAboutToHide()
    // タブの切り替えでは、隠れるページの OnPageAboutToHide() の前に OnWillCloseAllChildPages() が来ない。
    private const string MainMenuPagePrefix = "MainMenu";
    private const string CloseAllCall = "OnWillCloseAllChildPages()";
    private const string AboutToHideCall = "OnPageAboutToHide()";

    public static LogEvent Parse(in LogLine line)
    {
        if (line.Kind is LogLineKind.Undecodable)
            return LogEvent.Other(line);

        var m = line.Message;

        // 日時が壊れていても、現在地を変えるイベントかどうかは判定する。
        // 呼び出し側（VisitTracker）が日時の妥当性を見てUnknownへ倒す。
        if (m.StartsWith(JoiningPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.Joining, line, m[(Behaviour.Length + "Joining ".Length)..]);

        if (m.StartsWith(EnteringRoomPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.EnteringRoom, line, m[EnteringRoomPrefix.Length..]);

        if (string.Equals(m, JoinedRoomExact, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.JoinedRoom, line, string.Empty);

        if (m.StartsWith(JoiningOrCreatingPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.JoiningOrCreatingRoom, line, m[JoiningOrCreatingPrefix.Length..]);

        if (m.StartsWith(FinishedEnteringWorldPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.FinishedEnteringWorld, line, string.Empty);

        if (m.StartsWith(DestinationSetPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.DestinationSet, line, m[DestinationSetPrefix.Length..]);

        if (string.Equals(m, LeftRoomExact, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.LeftRoom, line, string.Empty);

        if (m.StartsWith(PlayerJoinedPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.PlayerJoined, line, m[PlayerJoinedPrefix.Length..]);

        if (m.StartsWith(PlayerLeftPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.PlayerLeft, line, m[PlayerLeftPrefix.Length..]);

        if (m.StartsWith(DisconnectedPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.Disconnected, line, m[DisconnectedPrefix.Length..]);

        if (m.StartsWith(CouldNotEnterPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.JoinFailed, line, m[CouldNotEnterPrefix.Length..]);

        if (m.StartsWith(FailedToJoinPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.JoinFailed, line, m[FailedToJoinPrefix.Length..]);

        if (IsQuit(m))
            return new LogEvent(LogEventKind.ApplicationQuit, line, string.Empty);

        if (m.StartsWith(MenuPrefix, StringComparison.Ordinal))
            return ParseMenuPage(line, m);

        if (m.StartsWith(ScreenshotPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.Screenshot, line, m[ScreenshotPrefix.Length..].Trim());

        if (m.StartsWith(UserAuthenticatedPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.UserAuthenticated, line, m[UserAuthenticatedPrefix.Length..].Trim());

        if (m.StartsWith(VrSdkPrefix, StringComparison.Ordinal))
            return new LogEvent(LogEventKind.VrSdkStarted, line, m[VrSdkPrefix.Length..].Trim());

        return LogEvent.Other(line);
    }

    /// <summary>正常終了の記録か。どちらの書き方でも終了として扱う。</summary>
    private static bool IsQuit(string message)
    {
        foreach (var prefix in QuitPrefixes)
        {
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// メニューのページの行（<c>VP &lt;ページ&gt; &lt;呼び出し&gt;()</c>）を読む。
    ///
    /// 対象のページを開いた（<see cref="LogEventKind.MainMenuPageShown"/>）かは、ページ名まで一致させるので、
    /// 別のページ（設定など）には反応しない。メインメニューを閉じたかは、メインメニューのどのページでも
    /// <c>OnWillCloseAllChildPages()</c> と <c>OnPageAboutToHide()</c> の並びで分かるので、その2つを別に返す（→実装メモ5.104）。
    /// それ以外のページの行は、並びが途切れたことを知らせるために <see cref="LogEventKind.MenuPageOther"/> で返す。
    /// </summary>
    private static LogEvent ParseMenuPage(in LogLine line, string message)
    {
        var rest = message.AsSpan(MenuPrefix.Length);
        var space = rest.IndexOf(' ');
        var page = space > 0 ? rest[..space].ToString() : string.Empty;
        var call = space > 0 ? rest[(space + 1)..] : ReadOnlySpan<char>.Empty;

        if (call.StartsWith(ShownCall, StringComparison.Ordinal) && MenuPages.Contains(page))
            return new LogEvent(LogEventKind.MainMenuPageShown, line, page);

        if (page.StartsWith(MainMenuPagePrefix, StringComparison.Ordinal))
        {
            if (call.SequenceEqual(CloseAllCall))
                return new LogEvent(LogEventKind.MainMenuClosing, line, page);

            if (call.SequenceEqual(AboutToHideCall))
                return new LogEvent(LogEventKind.MainMenuPageHiding, line, page);
        }

        return new LogEvent(LogEventKind.MenuPageOther, line, page);
    }
}
