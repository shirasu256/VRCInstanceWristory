namespace VRCInstanceWristory.Core.Locations;

/// <summary>インスタンスの種類。仕様5.2節。</summary>
public enum AccessType
{
    /// <summary>解析不能・未知・矛盾。既定で非表示・非記録。</summary>
    Unknown = 0,
    Public,
    GroupPublic,
    GroupOnly,
    GroupPlus,
    Friends,
    FriendsPlus,
    Invite,
    InvitePlus,
}

public static class AccessTypeExtensions
{
    /// <summary>仕様3.2節の表示名。</summary>
    public static string DisplayName(this AccessType t) => t switch
    {
        AccessType.Public => "Public",
        AccessType.GroupPublic => "Group Public",
        AccessType.GroupOnly => "Group",
        AccessType.GroupPlus => "Group+",
        AccessType.Friends => "Friends",
        AccessType.FriendsPlus => "Friends+",
        AccessType.Invite => "Invite",
        AccessType.InvitePlus => "Invite+",
        _ => "Unknown",
    };
}

/// <summary>
/// 記録する種類の組み合わせ（2026-09-26のユーザー指定でデスクトップのウィンドウから選べるようにした→実装メモ5.39）。
/// 解析できない・未知の種類（<see cref="AccessType.Unknown"/>）は選べず、常に対象外とする。
/// </summary>
public static class TargetAccessTypes
{
    /// <summary>
    /// 選べる種類。ウィンドウの並びもこの順にする（2列に並べるので、2つずつが1行になる）。
    /// Group+ と Group Only は 2026-09-27 のユーザー指定で入れ替えた（→実装メモ5.58）。
    /// </summary>
    public static readonly IReadOnlyList<AccessType> Selectable =
    [
        AccessType.Public,
        AccessType.GroupPublic,
        AccessType.GroupPlus,
        AccessType.GroupOnly,
        AccessType.FriendsPlus,
        AccessType.Friends,
        AccessType.InvitePlus,
        AccessType.Invite,
    ];

    /// <summary>既定の対象（仕様R03 の3種類に、2026-09-28 のユーザー指定で Group+ を足した→実装メモ5.86）。</summary>
    public static readonly IReadOnlySet<AccessType> Default =
        new HashSet<AccessType> { AccessType.Public, AccessType.GroupPublic, AccessType.GroupPlus, AccessType.GroupOnly };

    /// <summary>設定ファイルに書く名前（列挙子の名前そのもの。例: <c>GroupPublic</c>）。</summary>
    public static string SettingName(AccessType type) => type.ToString();

    /// <summary>設定ファイルの名前を種類へ直す。大文字小文字は問わない。知らない名前・Unknown は null。</summary>
    public static AccessType? ParseSettingName(string? name)
    {
        foreach (var type in Selectable)
        {
            if (string.Equals(SettingName(type), name?.Trim(), StringComparison.OrdinalIgnoreCase))
                return type;
        }

        return null;
    }
}
