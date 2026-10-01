namespace VRCInstanceWristory.Tests;

/// <summary>検証で使う location 文字列。</summary>
public static class Loc
{
    public const string WorldA = "wrld_00000000-0000-4000-9000-000000000005";
    public const string WorldB = "wrld_00000000-0000-4000-9000-000000000006";
    public const string GroupA = "grp_00000001-0000-4000-a000-000000000000";
    public const string GroupB = "grp_00000002-0000-4000-a000-000000000000";
    public const string User = "usr_00000000-0000-4000-8000-000000000001";

    public static string Public(string id, string world = WorldB) => $"{world}:{id}~region(jp)";

    public static string GroupPublic(string id, string group = GroupA, string world = WorldA)
        => $"{world}:{id}~group({group})~groupAccessType(public)~region(jp)";

    public static string GroupOnly(string id, string group = GroupA, string world = WorldA)
        => $"{world}:{id}~group({group})~groupAccessType(members)~region(jp)";

    public static string GroupPlus(string id, string group = GroupA, string world = WorldA)
        => $"{world}:{id}~group({group})~groupAccessType(plus)~region(jp)";

    public static string Friends(string id, string world = WorldB) => $"{world}:{id}~friends({User})~region(jp)";

    public static string FriendsPlus(string id, string world = WorldB) => $"{world}:{id}~hidden({User})~region(jp)";

    public static string Invite(string id, string world = WorldB) => $"{world}:{id}~private({User})~region(jp)";

    public static string InvitePlus(string id, string world = WorldB) => $"{world}:{id}~private({User})~canRequestInvite~region(jp)";
}
