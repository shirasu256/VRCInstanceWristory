using System.Drawing;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// グループ名の書き出しと読み込み（2026-09-27のユーザー指定→実装メモ5.65）。
/// ファイルを選ぶ画面（Win32）は出さず、ファイルの形と、ボタンが依頼を立てるところまでを確かめる。
/// </summary>
public class GroupNameFileTests
{
    private const string GroupA = "grp_00000090-0000-4000-a000-000000000000";
    private const string GroupB = "grp_00000000-1111-2222-3333-444444444444";

    [Fact]
    public void 書き出した形をそのまま読み戻せる()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GroupB] = "のんびり",
            [GroupA] = "まったり会",
        };

        var text = GroupNameFile.Serialize(names);

        // 設定ファイルと同じキーの下に、Group ID の順で並ぶ。日本語はそのまま書く。
        Assert.Contains("\"groupNames\"", text);
        Assert.Contains("まったり会", text);
        Assert.True(text.IndexOf(GroupB, StringComparison.Ordinal) < text.IndexOf(GroupA, StringComparison.Ordinal));

        var read = GroupNameFile.Parse(text, out var error);

        Assert.NotNull(read);
        Assert.Equal(string.Empty, error);
        Assert.Equal(0, read.Skipped);
        Assert.Equal(names, read.Names);
    }

    [Fact]
    public void 名前を並べただけの形とsettings_jsonも読める()
    {
        var flat = GroupNameFile.Parse($$"""{ "{{GroupA}}": "まったり会" }""", out _);
        Assert.Equal("まったり会", Assert.Single(flat!.Names).Value);

        // settings.json そのもの（ほかのキーやコメントのキーが混ざる）。
        var settings = GroupNameFile.Parse($$"""
            {
              "// グループ名": "説明",
              "retentionMinutes": 60,
              "groupNames": { "{{GroupA}}": "まったり会" },
            }
            """, out _);

        Assert.Equal(0, settings!.Skipped);
        Assert.Equal(GroupA, Assert.Single(settings.Names).Key);
    }

    [Fact]
    public void 使えない項目は飛ばして数える()
    {
        var read = GroupNameFile.Parse($$"""
            { "groupNames": {
                "{{GroupA}}": "  まったり会  ",
                "grp_not-a-uuid": "壊れたID",
                "{{GroupB}}": "{{new string('あ', AppSettings.MaxGroupNameLength + 1)}}",
                "grp_11111111-2222-3333-4444-555555555555": 12
            } }
            """, out _);

        Assert.NotNull(read);
        Assert.Equal(3, read.Skipped);

        // 前後の空白は設定と同じく落とす。
        Assert.Equal("まったり会", read.Names[GroupA]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"groupNames\": 3 }")]
    [InlineData("{ \"retentionMinutes\": 60 }")]
    public void グループ名のファイルでなければ理由を返す(string text)
    {
        Assert.Null(GroupNameFile.Parse(text, out var error));
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void ファイルへ書いて読み戻せる()
    {
        using var tempFile = new TempFile("groups");
        var path = tempFile.Path;

        GroupNameFile.Write(path, new Dictionary<string, string> { [GroupA] = "まったり会" });
        Assert.Equal("まったり会", GroupNameFile.Read(path, out _)!.Names[GroupA]);
    }

    [Fact]
    public void 読み込みと書き出しのボタンはデスクトップのウィンドウだけに出る()
    {
        var commands = new List<DesktopCommand>();
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        // グループ名は一般設定のタブの下のほう（→実装メモ5.71）。送らずに全部見える高さにする。
        view.Resize(new Size(1060, 1400), 1f);
        view.SelectTab(DesktopTab.Startup);

        var import = view.TargetRect(SettingsView.HitKind.GroupImport);
        var export = view.TargetRect(SettingsView.HitKind.GroupExport);
        Assert.NotNull(import);
        Assert.NotNull(export);
        Assert.True(import.Value.Right < export.Value.Left);

        // 押すと、ファイルを選ぶ画面を出してほしいという依頼が1回だけ立つ（設定は変えない）。
        Press(view, import.Value);
        Assert.Equal(GroupFileRequest.Import, view.TakeGroupFileRequest());
        Assert.Equal(GroupFileRequest.None, view.TakeGroupFileRequest());

        Press(view, export.Value);
        Assert.Equal(GroupFileRequest.Export, view.TakeGroupFileRequest());
        Assert.Empty(commands);

        // ダッシュボード（VRの中）では、デスクトップに出るファイルの画面は使えないので出さない。
        using var dashboard = new SettingsDashboardView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });
        Assert.Null(dashboard.TargetRect(SettingsView.HitKind.GroupImport));
        Assert.Null(dashboard.TargetRect(SettingsView.HitKind.GroupExport));
    }

    private static void Press(DesktopView view, RectangleF rect)
    {
        var point = new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));
        view.MouseMove(point);
        view.MouseDown(point);
        view.MouseUp();
    }
}
