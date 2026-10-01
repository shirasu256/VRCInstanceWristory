using System.Text.Json.Nodes;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// グループ名を手で付ける（2026-09-26のユーザー指定→実装メモ5.48）。
///
/// VRChat API は使わない（→5.26）。利用者が <c>grp_…</c> に付けた名前を、3段目の Group ID の代わりに出す。
/// 設定で「名前を付けたグループも Group ID を並べる」を選ぶと、名前の後ろに ID も出す。
/// </summary>
public class GroupNameTests
{
    [Fact]
    public void 名前を付けたグループはIDの代わりに名前を出す()
    {
        Assert.Equal($"Group Public · {Loc.GroupA}", RowFormatter.FormatType(AccessType.GroupPublic, Loc.GroupA));
        Assert.Equal("Group Public · まったり会", RowFormatter.FormatType(AccessType.GroupPublic, Loc.GroupA, "まったり会"));
        Assert.Equal($"Group Public · まったり会 · {Loc.GroupA}", RowFormatter.FormatType(AccessType.GroupPublic, Loc.GroupA, "まったり会", showIdWithName: true));

        // グループのないインスタンスは種類だけ。
        Assert.Equal("Public", RowFormatter.FormatType(AccessType.Public, null, "使われない"));
    }

    [Fact]
    public void 行を作るときに付けた名前を引く()
    {
        var record = new VisitRecord
        {
            EventId = "e",
            SourceSessionId = "s",
            SuccessByteOffset = 0,
            SessionOrder = 0,
            LocationKey = "k",
            WorldId = Loc.WorldA,
            InstanceId = "07254",
            AccessType = AccessType.GroupPublic,
            GroupId = Loc.GroupA,
            VisitedAtUtc = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc),
        };

        var groups = new GroupNaming(new Dictionary<string, string> { [Loc.GroupA] = "まったり会" });

        Assert.Equal("Group Public · まったり会", RowFormatter.Build([record], null, Core.LogTimeConverter.Local, record.VisitedAtUtc, groups: groups)[0].TypeText);
        Assert.Equal("まったり会", RowDetails.Build([record], null, groups)[0].GroupName);

        // 付けていないグループはIDのまま。
        Assert.Equal($"Group Public · {Loc.GroupA}", RowFormatter.Build([record], null, Core.LogTimeConverter.Local, record.VisitedAtUtc)[0].TypeText);
    }

    [Fact]
    public void 設定のグループ名は確かめてから使う()
    {
        var log = new CollectingDiagnostics();
        var settings = new AppSettings
        {
            GroupNames = new Dictionary<string, string>
            {
                [Loc.GroupA] = "  まったり会  ",
                [Loc.GroupB] = "",
                ["grp_not-a-uuid"] = "名前",
                [Loc.User] = "ユーザーIDは違う",
                ["grp_00000090-0000-4000-a000-000000000000"] = new string('あ', AppSettings.MaxGroupNameLength + 1),
            },
        };

        settings.Validate(log);

        Assert.Equal("まったり会", Assert.Single(settings.GroupNames).Value);
        Assert.Equal(4, log.Messages.Count(m => m.Contains("groupNames")));
    }

    [Theory]
    [InlineData("  名前  ", "名前")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("改行\nあり", null)]
    public void 付けようとする名前を整える(string input, string? expected)
        => Assert.Equal(expected, AppSettings.NormalizeGroupName(input));

    [Fact]
    public void グループ名は他の記述を残したまま書き戻す()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        File.WriteAllText(path, """
            {
              "// メモ": "利用者が書いた説明",
              "idFontPixels": 50
            }
            """);

        var settings = AppSettings.Load(path, new CollectingDiagnostics());
        settings.GroupNames[Loc.GroupA] = "まったり会";
        settings.SaveFields(path, SettingsField.GroupNames);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("利用者が書いた説明", (string?)root["// メモ"]);
        Assert.Equal("まったり会", (string?)root["groupNames"]![Loc.GroupA]);

        var reloaded = AppSettings.Load(path, new CollectingDiagnostics());
        Assert.Equal("まったり会", reloaded.GroupNames[Loc.GroupA]);
    }

    [Fact]
    public void Group_IDのリンクを押すとそのグループのページを開く()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        var point = SampleWindow.PointOfRow(view, rows, "86688");
        view.MouseMove(point);
        view.MouseDown(point);
        view.MouseUp();

        // 見本の 86688 は NAGiSA の Group（grp_09e67308-…）。リンクを押すと開く命令が出る（→実装メモ5.69）。実際には開かない。
        var link = view.DetailsTargetRect(RowDetailsView.HitKind.GroupLink)!.Value;
        view.MouseMove(SampleWindow.Center(link));
        Assert.True(view.IsClickable(SampleWindow.Center(link)));
        view.MouseDown(SampleWindow.Center(link));

        var open = Assert.IsType<DesktopCommand.OpenGroupPage>(Assert.Single(commands));
        Assert.StartsWith("grp_09e67308", open.GroupId);
        Assert.Equal($"https://vrchat.com/home/group/{open.GroupId}", Core.Locations.VrChatUrls.GroupPage(open.GroupId));
    }

    [Theory]
    [InlineData("grp_not-a-uuid")]
    [InlineData("grp_00000090-0000-4000-a000-000000000000/../x")]
    [InlineData("usr_00000000-0000-4000-8000-000000000090")]
    [InlineData(null)]
    public void 正しいGroup_IDでなければグループのページのURLを作らない(string? groupId)
        => Assert.Null(Core.Locations.VrChatUrls.GroupPage(groupId));

    [Fact]
    public void 選んだ行のグループ名の欄を押すと打ち込め_確定すると名前を送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        // 見本の 29719 のグループ（ひまり旅館の Group Public）には、まだ名前を付けていない（→実装メモ5.124）。
        var point = SampleWindow.PointOfRow(view, rows, "29719");
        view.MouseMove(point);
        view.MouseDown(point);
        view.MouseUp();

        // 「グループ名入力」のボタンはやめ、「未設定」の欄そのものを押す（→実装メモ5.67・5.68）。
        var field = view.DetailsTargetRect(RowDetailsView.HitKind.GroupName)!.Value;
        view.MouseMove(SampleWindow.Center(field));
        view.MouseDown(SampleWindow.Center(field));

        var request = view.TakeTextEditRequest();
        Assert.NotNull(request);
        Assert.Equal(string.Empty, request!.Initial);
        Assert.StartsWith("grp_344c2fbd", request.GroupId);

        // 打ち込む欄は、押した欄の中にそのまま置く。
        Assert.True(field.Contains(request.Rect));

        view.CommitGroupName(request.GroupId, "  かくにん会  ");

        var set = Assert.IsType<DesktopCommand.SetGroupName>(Assert.Single(commands));
        Assert.Equal("かくにん会", set.Name);

        // 空で確定すると名前を外す。
        view.CommitGroupName(request.GroupId, "   ");
        Assert.Null(Assert.IsType<DesktopCommand.SetGroupName>(commands[^1]).Name);
    }

    [Fact]
    public void グループでないインスタンスの行には名前を付ける部品を出さない()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        var point = SampleWindow.PointOfRow(view, rows, "db7da28295");
        view.MouseMove(point);
        view.MouseDown(point);
        view.MouseUp();

        Assert.Equal(DesktopTab.Details, view.Tab);
        Assert.Null(view.DetailsTargetRect(RowDetailsView.HitKind.GroupName));
    }
}
