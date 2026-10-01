namespace VRCInstanceWristory.Core.Logging;

/// <summary>
/// OnPlayerJoined / OnPlayerLeft / User Authenticated の「表示名 (userId)」を読んだもの。
/// </summary>
/// <param name="Key">
/// 在室者を見分ける鍵。表示名は変えられるうえ重複もし得るので、userId を取り出せたときは userId だけにする。
/// 書式が違って取り出せないときは、行の内容をそのまま鍵にする。
/// </param>
/// <param name="Name">表示名（→実装メモ5.46）。書式が違って取り出せないときは、行の内容をそのまま名前にする。</param>
public readonly record struct PlayerRef(string Key, string Name)
{
    public static PlayerRef Parse(string payload)
    {
        var whole = payload.Trim();
        var close = payload.LastIndexOf(')');
        if (close <= 0)
            return new PlayerRef(whole, whole);

        var open = payload.LastIndexOf('(', close - 1);
        if (open < 0)
            return new PlayerRef(whole, whole);

        var id = payload[(open + 1)..close].Trim();
        var name = payload[..open].Trim();
        return new PlayerRef(id.Length == 0 ? whole : id, name.Length == 0 ? whole : name);
    }
}
