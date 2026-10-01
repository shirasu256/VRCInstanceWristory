namespace VRCInstanceWristory.Core.Locations;

/// <summary>
/// vrchat.com のグループ・ユーザーのページ。VRChat API は使わず、ログから読んだIDから作るだけである。
/// ログから読んだ文字をそのまま URL へ入れないよう、正しいID（接頭辞と UUID）でなければ null を返す。
/// </summary>
public static class VrChatUrls
{
    /// <summary>グループのページ（2026-09-27のユーザー指定→実装メモ5.69）。</summary>
    public static string? GroupPage(string? groupId)
        => groupId is not null && LocationParser.IsValidGroupId(groupId) ? $"https://vrchat.com/home/group/{groupId}" : null;

    /// <summary>ユーザーのページ（2026-09-27のユーザー指定→実装メモ5.75）。</summary>
    public static string? UserPage(string? userId)
        => userId is not null && LocationParser.IsValidUserId(userId) ? $"https://vrchat.com/home/user/{userId}" : null;
}
