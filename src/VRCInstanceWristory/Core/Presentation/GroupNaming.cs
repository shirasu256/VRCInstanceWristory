namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 利用者が付けたグループ名と、その出し方（設定 <c>groupNames</c> / <c>showGroupIdWithName</c>・
/// 2026-09-26のユーザー指定→実装メモ5.48）。VRChat API は使わず、名前は利用者が付けたものだけを使う。
/// </summary>
/// <param name="Names">Group ID（<c>grp_…</c>）→ 付けた名前。</param>
/// <param name="ShowIdWithName">名前を付けたグループも、名前の後ろに Group ID を並べるか。既定は名前だけ。</param>
public sealed record GroupNaming(IReadOnlyDictionary<string, string> Names, bool ShowIdWithName = false)
{
    public static readonly GroupNaming None = new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>付けた名前。付けていなければ null。</summary>
    public string? NameOf(string? groupId)
        => groupId is not null && Names.TryGetValue(groupId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;
}
