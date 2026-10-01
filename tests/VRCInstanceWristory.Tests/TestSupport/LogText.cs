using System.Globalization;
using System.Text;
using VRCInstanceWristory.Core.Logging;

namespace VRCInstanceWristory.Tests;

/// <summary>検証用のログ本文を組み立てる。実ログと同じ書式を使う。</summary>
public static class LogText
{
    public static string Line(DateTime local, string message, string level = "Debug")
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0:yyyy.MM.dd HH:mm:ss} {1,-11}-  {2}\n",
            local,
            level,
            message);

    public static string Entering(DateTime t, string worldName) => Line(t, $"[Behaviour] Entering Room: {worldName}");

    public static string Joining(DateTime t, string location) => Line(t, $"[Behaviour] Joining {location}");

    public static string JoiningOrCreating(DateTime t, string worldName) => Line(t, $"[Behaviour] Joining or Creating Room: {worldName}");

    public static string Joined(DateTime t) => Line(t, "[Behaviour] Successfully joined room");

    /// <summary>
    /// 表示名から作る安定した userId。在室者は userId で見分けるので、名前ごとに別のIDにする。
    /// </summary>
    public static string UserId(string name)
        => "usr_" + new Guid(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(name)));

    /// <summary>実ログの OnPlayerJoined。入室直後に、居合わせた全員ぶんが並ぶ。</summary>
    public static string PlayerJoined(DateTime t, string name)
        => Line(t, $"[Behaviour] OnPlayerJoined {name} ({UserId(name)})");

    /// <summary>入室と、そのとき居合わせた people 人（自分を含む）の OnPlayerJoined。</summary>
    public static string JoinedWith(DateTime t, int people)
    {
        var text = Joined(t);

        for (var i = 0; i < people; i++)
            text += PlayerJoined(t, $"player{i}");

        return text;
    }

    public static string DestinationSet(DateTime t, string location) => Line(t, $"[Behaviour] Destination set: {location}");

    public static string LeftRoom(DateTime t) => Line(t, "[Behaviour] OnLeftRoom");

    /// <summary>実ログの OnPlayerLeft。退出直後に人数ぶん並ぶ。</summary>
    public static string PlayerLeft(DateTime t, string name)
        => Line(t, $"[Behaviour] OnPlayerLeft {name} ({UserId(name)})");

    /// <summary>
    /// 退出とその直後の OnPlayerLeft の並び。people は自分を含むインスタンスの人数。
    /// 実ログでは OnLeftRoom → OnPlayerLeft×人数 → 世界の後始末、の順に出る。
    /// 名前は <see cref="JoinedWith"/> と揃えてあるので、在室者がちょうど空になる。
    /// </summary>
    public static string LeftRoomWith(DateTime t, int people)
    {
        var text = LeftRoom(t);

        for (var i = 0; i < people; i++)
            text += PlayerLeft(t, $"player{i}");

        return text;
    }

    public static string Disconnected(DateTime t, string reason = "DisconnectByClientLogic") => Line(t, $"[Behaviour] OnDisconnected: {reason}");

    public static string CouldNotEnter(DateTime t, string reason) => Line(t, $"[Behaviour] Could not enter room because: {reason}", "Error");

    public static string FailedToJoin(DateTime t, string location) => Line(t, $"Failed to join user in instance {location} - Instance is full", "Error");

    public static string Quit(DateTime t) => Line(t, "VRCApplication: HandleApplicationQuit at 1234.5");

    /// <summary>版によってはこちらが書かれる。どちらも正常終了として扱う。</summary>
    public static string QuitAlternate(DateTime t) => Line(t, "VRCApplication: OnApplicationQuit at 1234.5");

    public static string Noise(DateTime t) => Line(t, "[Behaviour] Waiting to enter network room.");

    /// <summary>ログインしている利用者本人（実ログどおり接頭辞なし）。「一緒にいた人」から自分を除くのに使う。</summary>
    public static string UserAuthenticated(DateTime t, string name) => Line(t, $"User Authenticated: {name} ({UserId(name)})");

    /// <summary>写真の保存（実ログどおり、保存先の区切りに / と \ が混ざる）。</summary>
    public static string Screenshot(DateTime t, string path) => Line(t, $"[VRC Camera] Took screenshot to: {path}");

    /// <summary>ロード画面の終わり。実ログでは入室の確定から数秒あとに出る。</summary>
    public static string FinishedEntering(DateTime t) => Line(t, "[Behaviour] Finished entering world.");

    /// <summary>メインメニューのページを開いた（実ログどおり Warning レベル・接頭辞なし）。</summary>
    public static string WorldsTabShown(DateTime t, string page = "MainMenuWorlds")
        => Line(t, $"VP {page} OnPageAboutToShow()", "Warning");

    /// <summary>メインメニューのページを閉じた。実ログでは AboutToHide と Hidden が続けて出る。</summary>
    public static string WorldsTabHidden(DateTime t, string page = "MainMenuWorlds")
        => Line(t, $"VP {page} OnPageAboutToHide()", "Warning")
           + Line(t, $"VP {page} OnPageHidden()", "Warning");

    /// <summary>
    /// メインメニューを閉じた（B / Y の短押し・片方を押し続けたままのもう片方の短押しなど）。
    /// 実ログ（2026-10-01）どおり、開いていたページの OnWillCloseAllChildPages の直後に AboutToHide・Hidden・Finish が続く（→実装メモ5.104）。
    /// 間に挟まる Debug の行も実ログに合わせて入れる。
    /// </summary>
    public static string MainMenuClosed(DateTime t, string page = "MainMenuWorlds")
        => Line(t, $"VP {page} OnWillCloseAllChildPages()", "Warning")
           + Line(t, "ÍÏÌÎÎÌÍÎÌÎÏÏÏÌÍÍÎÌÎÎÏÌÌ finished")
           + Line(t, $"VP {page} OnPageAboutToHide()", "Warning")
           + Line(t, $"VP {page} OnPageHidden()", "Warning")
           + Line(t, $"VP {page} Finish()", "Warning");

    /// <summary>
    /// Entering → Joining → Joining or Creating → 成功 → ロード完了 の一連。
    /// people を渡すと、入室直後の OnPlayerJoined（自分を含む在室者）も続ける。
    /// 実ログと同じく、末尾に `Finished entering world.`（ロード画面の終わり）を置く。
    /// </summary>
    public static string Visit(DateTime t, string location, string worldName, int people = 0)
        => Entering(t, worldName) + Joining(t, location) + JoiningOrCreating(t, worldName)
           + (people > 0 ? JoinedWith(t, people) : Joined(t))
           + FinishedEntering(t);

    /// <summary>ログの本文を1行ずつ解析する（行頭のバイト位置も実ログと同じに数える）。</summary>
    public static IEnumerable<LogEvent> Events(string text)
    {
        var offset = 0L;
        var number = 0;

        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetByteCount(raw) + 1;
            var line = LogLineParser.Parse(raw, offset, ++number) with { ByteLength = bytes };
            offset += bytes;
            yield return LogEventParser.Parse(line);
        }
    }

    /// <summary>離脱を挟んだ移動。</summary>
    public static string Move(DateTime t, string location, string worldName, int people = 0)
        => DestinationSet(t.AddSeconds(-1), location) + LeftRoom(t.AddSeconds(-1)) + Visit(t, location, worldName, people);
}
