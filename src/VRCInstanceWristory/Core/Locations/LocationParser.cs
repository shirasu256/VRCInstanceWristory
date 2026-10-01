using System.Text;

namespace VRCInstanceWristory.Core.Locations;

/// <summary>
/// location文字列の解析と種類判定（仕様5節）。
/// 例: wrld_00000000-...-000000000006:29719~group(grp_...)~groupAccessType(public)~region(jp)
/// 未知・矛盾・欠損はUnknownとし、推測でPublicへ分類しない。
/// </summary>
public static class LocationParser
{
    public const int MaxInstanceIdLength = 128; // 実装上の入力防御。VRChatの公式上限ではない。

    private const string WorldPrefix = "wrld_";
    public const string GroupPrefix = "grp_";
    public const string UserPrefix = "usr_";

    /// <summary>値を取るキー。</summary>
    private static readonly string[] ValueKeys =
        ["group", "groupAccessType", "friends", "hidden", "private", "region", "nonce"];

    /// <summary>値を取らないフラグ。</summary>
    private static readonly string[] FlagKeys = ["ageGate", "strict", "canRequestInvite"];

    /// <summary>同時に複数を認めないアクセス指定の系列。</summary>
    private static readonly string[] RestrictionKeys = ["group", "friends", "hidden", "private"];

    public static ParsedLocation Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return ParsedLocation.Invalid(raw ?? string.Empty, "empty");

        // 行末の空白のみ除去する。
        var text = raw.TrimEnd(' ', '\t');
        if (text.Length == 0)
            return ParsedLocation.Invalid(raw, "empty");

        var colon = text.IndexOf(':');
        if (colon <= 0)
            return ParsedLocation.Invalid(raw, "no-world-separator");

        var worldId = text[..colon];
        if (!TryNormalizeWorldId(worldId, out var normalizedWorldId))
            return ParsedLocation.Invalid(raw, "bad-world-id");

        var rest = text[(colon + 1)..];
        var tilde = rest.IndexOf('~');
        var instanceId = tilde < 0 ? rest : rest[..tilde];

        if (!IsValidInstanceId(instanceId))
            return ParsedLocation.Invalid(raw, "bad-instance-id");

        var tags = new List<LocationTag>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        if (tilde >= 0)
        {
            var tagPart = rest[(tilde + 1)..];
            foreach (var segment in tagPart.Split('~'))
            {
                if (segment.Length == 0)
                    return ParsedLocation.Invalid(raw, "empty-tag");

                if (!TryParseTag(segment, out var tag, out var reason))
                    return ParsedLocation.Invalid(raw, reason);

                if (!seenKeys.Add(tag.Key))
                    return ParsedLocation.Invalid(raw, "duplicate-tag:" + tag.Key);

                tags.Add(tag);
            }
        }

        var accessType = Classify(tags, out var classifyReason);
        if (accessType == AccessType.Unknown)
            return ParsedLocation.Invalid(raw, classifyReason);

        var groupId = FindValue(tags, "group");
        var region = FindValue(tags, "region");

        return new ParsedLocation
        {
            Raw = raw,
            IsValid = true,
            WorldId = normalizedWorldId,
            InstanceId = instanceId,
            Tags = tags,
            AccessType = accessType,
            GroupId = groupId,
            Region = region,
            LocationKey = BuildLocationKey(normalizedWorldId, instanceId, tags),
        };
    }

    /// <summary>仕様5.3節のlocationKey。タグ順の違いを吸収し、値の大小文字は保存する。</summary>
    public static string BuildLocationKey(string worldId, string instanceId, IReadOnlyList<LocationTag> tags)
    {
        var sb = new StringBuilder();
        sb.Append(worldId).Append(':').Append(instanceId);

        foreach (var tag in tags.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            sb.Append('~').Append(tag.Key);
            if (tag.Value is not null)
                sb.Append('(').Append(tag.Value).Append(')');
        }

        return sb.ToString();
    }

    private static AccessType Classify(List<LocationTag> tags, out string reason)
    {
        reason = string.Empty;

        var restrictionCount = 0;
        foreach (var t in tags)
        {
            if (RestrictionKeys.Contains(t.Key, StringComparer.Ordinal))
                restrictionCount++;
        }

        if (restrictionCount > 1)
        {
            reason = "conflicting-access";
            return AccessType.Unknown;
        }

        var groupAccess = FindValue(tags, "groupAccessType");
        var group = FindValue(tags, "group");

        // group と groupAccessType は必ず対。
        if (group is null != (groupAccess is null))
        {
            reason = "group-pair-missing";
            return AccessType.Unknown;
        }

        if (group is not null)
        {
            if (!IsValidGroupId(group))
            {
                reason = "bad-group-id";
                return AccessType.Unknown;
            }

            switch (groupAccess)
            {
                case "public":
                    return AccessType.GroupPublic;
                case "members":
                    return AccessType.GroupOnly;
                case "plus":
                    return AccessType.GroupPlus;
                default:
                    reason = "unknown-group-access";
                    return AccessType.Unknown;
            }
        }

        if (HasKey(tags, "friends"))
            return AccessType.Friends;

        if (HasKey(tags, "hidden"))
            return AccessType.FriendsPlus;

        if (HasKey(tags, "private"))
            return HasKey(tags, "canRequestInvite") ? AccessType.InvitePlus : AccessType.Invite;

        // ここから先が Public 判定（ほかの条件をすべて検査した後に行う）。
        // canRequestInvite 単独、nonce・strict だけの location は Public にしない。
        if (HasKey(tags, "canRequestInvite"))
        {
            reason = "can-request-invite-without-private";
            return AccessType.Unknown;
        }

        if (HasKey(tags, "nonce") || HasKey(tags, "strict"))
        {
            reason = "restriction-helper-without-access";
            return AccessType.Unknown;
        }

        return AccessType.Public;
    }

    private static bool TryParseTag(string segment, out LocationTag tag, out string reason)
    {
        tag = default;
        reason = string.Empty;

        var open = segment.IndexOf('(');
        if (open < 0)
        {
            // フラグ
            if (!FlagKeys.Contains(segment, StringComparer.Ordinal))
            {
                reason = "unknown-flag:" + segment;
                return false;
            }

            tag = new LocationTag(segment, null);
            return true;
        }

        if (segment[^1] != ')')
        {
            reason = "unclosed-tag";
            return false;
        }

        var key = segment[..open];
        var value = segment[(open + 1)..^1];

        if (key.Length == 0 || value.Length == 0)
        {
            reason = "empty-tag-part";
            return false;
        }

        if (value.IndexOf('(') >= 0 || value.IndexOf(')') >= 0)
        {
            reason = "nested-parens";
            return false;
        }

        if (!ValueKeys.Contains(key, StringComparer.Ordinal))
        {
            reason = "unknown-tag:" + key;
            return false;
        }

        tag = new LocationTag(key, value);
        return true;
    }

    private static bool HasKey(List<LocationTag> tags, string key)
    {
        foreach (var t in tags)
        {
            if (string.Equals(t.Key, key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string? FindValue(IReadOnlyList<LocationTag> tags, string key)
    {
        foreach (var t in tags)
        {
            if (string.Equals(t.Key, key, StringComparison.Ordinal))
                return t.Value;
        }

        return null;
    }

    public static bool IsValidInstanceId(string id)
    {
        if (id.Length is 0 or > MaxInstanceIdLength)
            return false;

        foreach (var c in id)
        {
            var ok = c is >= '0' and <= '9' || c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z';
            if (!ok)
                return false;
        }

        return true;
    }

    public static bool TryNormalizeWorldId(string worldId, out string normalized)
    {
        normalized = string.Empty;
        if (!worldId.StartsWith(WorldPrefix, StringComparison.Ordinal))
            return false;

        var uuid = worldId[WorldPrefix.Length..];
        if (!IsUuid(uuid))
            return false;

        normalized = WorldPrefix + uuid.ToLowerInvariant();
        return true;
    }

    public static bool IsValidGroupId(string groupId)
        => groupId.StartsWith(GroupPrefix, StringComparison.Ordinal) && IsUuid(groupId.AsSpan(GroupPrefix.Length));

    /// <summary>正しいユーザーID（usr_ と UUID）か。</summary>
    public static bool IsValidUserId(string userId)
        => userId.StartsWith(UserPrefix, StringComparison.Ordinal) && IsUuid(userId.AsSpan(UserPrefix.Length));

    /// <summary>8-4-4-4-12 の16進数。</summary>
    private static bool IsUuid(ReadOnlySpan<char> s)
    {
        if (s.Length != 36)
            return false;

        ReadOnlySpan<int> groups = [8, 4, 4, 4, 12];
        var pos = 0;

        for (var g = 0; g < groups.Length; g++)
        {
            if (g > 0)
            {
                if (s[pos] != '-')
                    return false;

                pos++;
            }

            for (var i = 0; i < groups[g]; i++)
            {
                if (!IsHex(s[pos + i]))
                    return false;
            }

            pos += groups[g];
        }

        return pos == s.Length;
    }

    private static bool IsHex(char c)
        => c is >= '0' and <= '9' || c is >= 'a' and <= 'f' || c is >= 'A' and <= 'F';
}
