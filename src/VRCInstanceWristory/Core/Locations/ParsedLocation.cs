namespace VRCInstanceWristory.Core.Locations;

/// <summary>location文字列の解析結果。無効なら <see cref="IsValid"/> が false で、種類はUnknown。</summary>
public sealed class ParsedLocation
{
    public required string Raw { get; init; }
    public bool IsValid { get; init; }
    public string? InvalidReason { get; init; }

    /// <summary>wrld_ + UUID。UUID部分は小文字へ正規化する（仕様5.3節）。</summary>
    public string WorldId { get; init; } = string.Empty;

    /// <summary>表示ID。大小文字・先頭ゼロをそのまま保つ（R07）。</summary>
    public string InstanceId { get; init; } = string.Empty;

    public IReadOnlyList<LocationTag> Tags { get; init; } = Array.Empty<LocationTag>();

    public AccessType AccessType { get; init; } = AccessType.Unknown;

    /// <summary>完全なGroup ID。短縮は描画時に導出する。</summary>
    public string? GroupId { get; init; }

    public string? Region { get; init; }

    /// <summary>同一判定・回数・保存に使うキー（仕様5.3節）。無効な場合は空。</summary>
    public string LocationKey { get; init; } = string.Empty;

    public static ParsedLocation Invalid(string raw, string reason) => new()
    {
        Raw = raw,
        IsValid = false,
        InvalidReason = reason,
        AccessType = AccessType.Unknown,
    };

    /// <summary>`grp_` を除いたUUID先頭8文字に `g:` を付けた短縮表示（仕様3.2節の実装案）。</summary>
    public string? ShortGroupId => FormatShortGroupId(GroupId);

    public static string? FormatShortGroupId(string? groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return null;

        var prefix = LocationParser.GroupPrefix;
        var body = groupId.StartsWith(prefix, StringComparison.Ordinal) ? groupId[prefix.Length..] : groupId;
        return "g:" + (body.Length <= 8 ? body : body[..8]);
    }

    public override string ToString() => $"{InstanceId} ({AccessType.DisplayName()})";
}
