using VRCInstanceWristory.Core.Locations;

namespace VRCInstanceWristory.Tests;

/// <summary>仕様5節（location解析・種類判定）と受入項目 T01〜T05・T23・L07。</summary>
public class LocationParserTests
{
    [Theory]
    // T01: 対象3種別
    [InlineData("wrld_00000000-0000-4000-9000-000000000005:07254~group(grp_00000001-0000-4000-a000-000000000000)~groupAccessType(public)~region(jp)", AccessType.GroupPublic, true)]
    [InlineData("wrld_00000000-0000-4000-9000-000000000001:86688~group(grp_00000090-0000-4000-a000-000000000000)~groupAccessType(members)~region(us)", AccessType.GroupOnly, true)]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~region(jp)", AccessType.Public, true)]
    // T01: 対象外5種別
    [InlineData("wrld_00000000-0000-4000-9000-000000000001:99346~group(grp_00000090-0000-4000-a000-000000000000)~groupAccessType(plus)~region(us)", AccessType.GroupPlus, true)]
    [InlineData("wrld_00000000-0000-4000-9000-000000000007:38046~friends(usr_00000000-0000-4000-8000-000000000211)~region(jp)", AccessType.Friends, false)]
    [InlineData("wrld_00000000-0000-4000-9000-000000000008:04321~hidden(usr_00000000-0000-4000-8000-000000000213)~region(jp)", AccessType.FriendsPlus, false)]
    [InlineData("wrld_00000000-0000-4000-9000-000000000012:26810~private(usr_00000000-0000-4000-8000-000000000001)~region(jp)", AccessType.Invite, false)]
    [InlineData("wrld_00000000-0000-4000-9000-000000000012:26810~private(usr_00000000-0000-4000-8000-000000000001)~canRequestInvite~region(jp)", AccessType.InvitePlus, false)]
    public void 種類を判定する(string raw, AccessType expected, bool isTarget)
    {
        var location = LocationParser.Parse(raw);

        Assert.True(location.IsValid, location.InvalidReason);
        Assert.Equal(expected, location.AccessType);
        Assert.Equal(isTarget, TargetAccessTypes.Default.Contains(location.AccessType));
    }

    [Theory]
    // T02: 未知タグ・欠損・矛盾・不正ID は Unknown とし、Public と誤判定しない。
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~unknownTag(x)")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~group(grp_00000002-0000-4000-a000-000000000000)")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~groupAccessType(public)")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~group(grp_00000002-0000-4000-a000-000000000000)~groupAccessType(secret)")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~group(nothex)~groupAccessType(public)")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~friends(usr_x)~private(usr_y)")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~region()")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~region(jp")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~region(jp)~region(us)")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~canRequestInvite")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~nonce(abc)")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24688~strict")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006:24-688")]
    [InlineData("wrld_00000006:24688")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000006")]
    [InlineData("")]
    public void 未知や矛盾はUnknownにする(string raw)
    {
        var location = LocationParser.Parse(raw);

        Assert.False(location.IsValid);
        Assert.Equal(AccessType.Unknown, location.AccessType);
        Assert.DoesNotContain(location.AccessType, TargetAccessTypes.Default);
    }

    [Fact]
    public void T03_タグ順を変えても分類は変わらない()
    {
        var a = LocationParser.Parse($"{Loc.WorldA}:07254~group({Loc.GroupA})~groupAccessType(public)~region(jp)");
        var b = LocationParser.Parse($"{Loc.WorldA}:07254~region(jp)~groupAccessType(public)~group({Loc.GroupA})");

        Assert.Equal(a.AccessType, b.AccessType);
        Assert.Equal(a.LocationKey, b.LocationKey);
    }

    [Fact]
    public void T03_地域やageGateだけでは対象から外さない()
    {
        var us = LocationParser.Parse($"{Loc.WorldB}:24688~region(us)");
        var ageGate = LocationParser.Parse($"{Loc.WorldB}:24688~region(jp)~ageGate");

        Assert.Equal(AccessType.Public, us.AccessType);
        Assert.Equal(AccessType.Public, ageGate.AccessType);
        Assert.Contains(ageGate.AccessType, TargetAccessTypes.Default);

        // 地域が違えば別のキーになる。
        Assert.NotEqual(us.LocationKey, LocationParser.Parse($"{Loc.WorldB}:24688~region(jp)").LocationKey);
    }

    [Theory]
    // T04: 先頭ゼロ・大小文字・10文字IDをそのまま保つ。
    [InlineData("00001")]
    [InlineData("A0b9")]
    [InlineData("c1e88b6419")]
    [InlineData("07254")]
    public void T04_表示IDを変えずに保持する(string id)
    {
        var location = LocationParser.Parse($"{Loc.WorldB}:{id}~region(jp)");

        Assert.True(location.IsValid);
        Assert.Equal(id, location.InstanceId);
    }

    [Fact]
    public void T04_128文字を超える表示IDは受け付けない()
    {
        var ok = LocationParser.Parse($"{Loc.WorldB}:{new string('a', 128)}~region(jp)");
        var tooLong = LocationParser.Parse($"{Loc.WorldB}:{new string('a', 129)}~region(jp)");

        Assert.True(ok.IsValid);
        Assert.False(tooLong.IsValid);
    }

    [Fact]
    public void T05_同じ短いIDでもワールドやグループが違えば別のキーになる()
    {
        var a = LocationParser.Parse(Loc.GroupPublic("07254", Loc.GroupA, Loc.WorldA));
        var b = LocationParser.Parse(Loc.GroupPublic("07254", Loc.GroupA, Loc.WorldB));
        var c = LocationParser.Parse(Loc.GroupPublic("07254", Loc.GroupB, Loc.WorldA));

        Assert.NotEqual(a.LocationKey, b.LocationKey);
        Assert.NotEqual(a.LocationKey, c.LocationKey);
    }

    [Fact]
    public void T23_短縮GroupIDはgrp_を除いた先頭8文字にする()
    {
        var location = LocationParser.Parse(Loc.GroupPublic("07254"));

        Assert.Equal("g:00000001", location.ShortGroupId);
        Assert.Equal(Loc.GroupA, location.GroupId);
    }

    [Fact]
    public void T23_PublicにはGroupIDを付けない()
    {
        var location = LocationParser.Parse(Loc.Public("24688"));

        Assert.Null(location.GroupId);
        Assert.Null(location.ShortGroupId);
    }

    [Fact]
    public void T23_短縮表示が同じでも完全なGroupIDが違えば別キーになる()
    {
        const string groupX = "grp_00000001-0000-0000-0000-000000000001";
        const string groupY = "grp_00000001-0000-0000-0000-000000000002";

        var x = LocationParser.Parse(Loc.GroupPublic("07254", groupX));
        var y = LocationParser.Parse(Loc.GroupPublic("07254", groupY));

        Assert.Equal(x.ShortGroupId, y.ShortGroupId);
        Assert.NotEqual(x.LocationKey, y.LocationKey);
    }

    [Fact]
    public void L07_追加提供された2行の種類を判定する()
    {
        var members = LocationParser.Parse("wrld_00000000-0000-4000-9000-000000000001:86688~group(grp_00000090-0000-4000-a000-000000000000)~groupAccessType(members)~region(us)");
        var plus = LocationParser.Parse("wrld_00000000-0000-4000-9000-000000000001:99346~group(grp_00000090-0000-4000-a000-000000000000)~groupAccessType(plus)~region(us)");

        Assert.Equal(AccessType.GroupOnly, members.AccessType);
        Assert.Contains(members.AccessType, TargetAccessTypes.Default);

        Assert.Equal(AccessType.GroupPlus, plus.AccessType);
        Assert.Contains(plus.AccessType, TargetAccessTypes.Default); // 2026-09-28 から既定で記録する（→実装メモ5.86）
    }

    [Fact]
    public void worldIDの大文字は小文字へ正規化し表示IDは保つ()
    {
        var upper = LocationParser.Parse("wrld_ABCDEF00-0000-4000-9000-00000000000A:A0b9~region(jp)");
        var lower = LocationParser.Parse("wrld_abcdef00-0000-4000-9000-00000000000a:A0b9~region(jp)");

        Assert.Equal(lower.LocationKey, upper.LocationKey);
        Assert.Equal("A0b9", upper.InstanceId);
    }

    [Fact]
    public void タグ名とアクセス値は大小文字まで一致させる()
    {
        Assert.False(LocationParser.Parse($"{Loc.WorldA}:1~group({Loc.GroupA})~GroupAccessType(public)").IsValid);
        Assert.False(LocationParser.Parse($"{Loc.WorldA}:1~group({Loc.GroupA})~groupAccessType(Public)").IsValid);
    }
}
