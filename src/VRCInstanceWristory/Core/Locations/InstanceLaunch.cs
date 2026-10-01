namespace VRCInstanceWristory.Core.Locations;

/// <summary>
/// 行からそのインスタンスを開くための URL（2026-09-26のユーザー指定→実装メモ5.43・5.53）。
///
/// VRChat API は使わない。どちらもログに書かれていた location から作るだけである。
/// <list type="bullet">
/// <item>Webページ（<see cref="WebUrlFor"/>）: vrchat.com の共有リンクと同じ形（<c>/home/launch?worldId=…&amp;instanceId=…</c>）。既定</item>
/// <item>launch URL（<see cref="UrlFor"/>）: VRChat が登録している URL スキーム（<c>vrchat://launch</c>）。
/// Webサイトの「Launch World」と同じ形で、VRChat はクライアントを起動し直す</item>
/// </list>
///
/// location はログから読んだものなので、そのまま URL に入れる前に<b>解析できる location であること</b>と、
/// 使う文字が location の書式に出てくるもの（英数字と <c>_-:~().</c>）だけであることを確かめる。
/// それ以外（空白・<c>&amp;</c>・<c>#</c> など）が混ざったものは、URL の区切りとして読まれかねないので開かない。
/// </summary>
public static class InstanceLaunch
{
    public const string Scheme = "vrchat://launch";

    /// <summary>launch URL の <c>ref</c>。VRChat の Webサイトが付けているものと同じ。</summary>
    public const string Referrer = "vrchat.com";

    /// <summary>インスタンスのWebページ（vrchat.com の共有リンク）の前半。</summary>
    public const string WebPage = "https://vrchat.com/home/launch";

    /// <summary>location から launch URL（<c>vrchat://launch</c>）を作る。開いてはいけない location なら null。</summary>
    public static string? UrlFor(string? location)
        => Checked(location) is { } text ? $"{Scheme}?ref={Referrer}&id={text}" : null;

    /// <summary>location からインスタンスのWebページの URL を作る。開いてはいけない location なら null。</summary>
    public static string? WebUrlFor(string? location)
    {
        if (Checked(location) is not { } text)
            return null;

        // 解析できた location は必ず「ワールドID:インスタンス」の形をしている。
        // 使う文字は URL のクエリの中で区切りにならないものだけに限ってあるので（→Checked）、
        // VRChat の Webサイトが出す共有リンクと同じく、そのまま入れる。
        var colon = text.IndexOf(':');
        var world = text[..colon];
        var instance = text[(colon + 1)..];

        return $"{WebPage}?worldId={world}&instanceId={instance}";
    }

    /// <summary>その location の URL を作れるか（どちらの URL も同じ条件）。</summary>
    public static bool CanOpen(string? location) => Checked(location) is not null;

    /// <summary>URL に入れてよい location なら前後の空白を落としたものを、そうでなければ null を返す。</summary>
    private static string? Checked(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        var text = location.Trim();

        foreach (var c in text)
        {
            if (!IsAllowed(c))
                return null;
        }

        if (!LocationParser.Parse(text).IsValid || text.IndexOf(':') <= 0)
            return null;

        return text;
    }

    private static bool IsAllowed(char c)
        => c is >= '0' and <= '9'
            or >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or '_' or '-' or ':' or '~' or '(' or ')' or '.';
}
