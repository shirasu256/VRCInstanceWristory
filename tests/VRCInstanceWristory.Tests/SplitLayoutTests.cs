using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// ステータスの段の右端のリンクと、パネルの枠の高さ（→実装メモ5.71）。ウィンドウ（Win32）は開かない。
/// 幅が足りるときに「インスタンス詳細」を独立させるのは 5.73 でやめた。
/// </summary>
public class SplitLayoutTests
{
    private static DesktopView Window(List<DesktopCommand> commands, Size size)
    {
        var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        view.Resize(size, 1f);
        view.SetRows(SampleRows.Build());
        view.SetDetails(SampleRows.Details());

        // ウィンドウは最初に描くときに並べる。
        _ = view.PanelRect;
        return view;
    }

    [Fact]
    public void 横に長いウィンドウでは_パネルが等倍になった後の余りを右側が埋める()
    {
        var commands = new List<DesktopCommand>();
        using var view = Window(commands, DesktopView.DefaultClientSize);
        var style = new PanelStyle();

        // 既定の大きさでは右側は既定の幅（344論理px）で、パネルがそれ以外を使う。
        var normal = view.TabRect(DesktopTab.Startup).Right - view.TabRect(DesktopTab.Details).X;
        Assert.Equal(SettingsView.ColumnWidth, normal, 1);

        // 高さに余裕のある、うんと横に長いウィンドウ。パネルは等倍（960論理px）で止まり、右側が残りを埋める（→実装メモ5.74）。
        view.Resize(new Size(2400, 1200), 1f);
        Assert.Equal(style.Width, view.PanelRect.Width, 1);

        var tabsLeft = view.TabRect(DesktopTab.Details).X;
        var tabsRight = view.TabRect(DesktopTab.Startup).Right;
        Assert.Equal(2400 - 16, tabsRight, 1);
        Assert.Equal(view.PanelRect.Right + 16, tabsLeft, 1);

        view.SelectTab(DesktopTab.Startup);
        var plus = view.TargetRect(SettingsView.HitKind.StepperPlus, 0)!.Value;
        Assert.True(plus.Right > 2400 - 60); // 設定の部品も広げた列の右端まで
    }

    [Fact]
    public void 選んでいる行をもう一度押すと選んでいない状態へ戻る()
    {
        var commands = new List<DesktopCommand>();
        using var view = Window(commands, DesktopView.DefaultClientSize);
        var row = view.RowScreenRect(7);
        var point = new PointF(view.PanelRect.X + (view.PanelRect.Width * 0.42f), row.Y + (row.Height / 2f));

        view.MouseDown(point);
        view.MouseUp();
        Assert.NotNull(view.SelectedEventId);
        Assert.NotNull(view.DetailsTargetRect(RowDetailsView.HitKind.Return));

        // もう一度押すと、インスタンス詳細は最初の状態（→実装メモ5.75）。
        view.MouseDown(point);
        view.MouseUp();
        Assert.Null(view.SelectedEventId);
        Assert.Equal(DesktopTab.Details, view.Tab);
        Assert.Null(view.DetailsTargetRect(RowDetailsView.HitKind.Return));
        Assert.Empty(commands);
    }

    [Fact]
    public void グループはGroup_IDを上に名前の欄を下に置き_ユーザーの名前はページを開く()
    {
        var commands = new List<DesktopCommand>();
        using var view = Window(commands, DesktopView.DefaultClientSize);
        var row = view.RowScreenRect(7); // 86688（Group・一緒にいた人が4人）
        view.MouseDown(new PointF(view.PanelRect.X + (view.PanelRect.Width * 0.42f), row.Y + (row.Height / 2f)));
        view.MouseUp();

        var link = view.DetailsTargetRect(RowDetailsView.HitKind.GroupLink)!.Value;
        var field = view.DetailsTargetRect(RowDetailsView.HitKind.GroupName)!.Value;
        Assert.True(link.Bottom <= field.Y);

        var person = view.DetailsTargetRect(RowDetailsView.HitKind.Person, 1)!.Value;
        var center = new PointF(person.X + 4f, person.Y + (person.Height / 2f));
        Assert.True(view.IsClickable(center));
        view.MouseDown(center);

        var open = Assert.IsType<DesktopCommand.OpenUserPage>(Assert.Single(commands));
        Assert.StartsWith("usr_", open.UserId);
    }

    [Theory]
    [InlineData("usr_00000000-0000-4000-8000-000000000001", "https://vrchat.com/home/user/usr_00000000-0000-4000-8000-000000000001")]
    [InlineData("usr_not-a-uuid", null)]
    [InlineData("grp_00000091-0000-4000-a000-000000000000", null)]
    [InlineData("usr_00000000-0000-4000-8000-000000000001/../x", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void 正しいユーザーIDのときだけユーザーのページのURLを作る(string? userId, string? expected)
        => Assert.Equal(expected, Core.Locations.VrChatUrls.UserPage(userId));

    [Fact]
    public void ステータスの段の右端の名前は開発者のページを開く()
    {
        var commands = new List<DesktopCommand>();
        using var view = Window(commands, DesktopView.DefaultClientSize);

        var link = view.CreditLinkRect;
        Assert.True(link.Right <= view.PanelRect.Right + 0.5f);
        Assert.True(link.Y >= view.PanelRect.Bottom);

        var center = new PointF(link.X + (link.Width / 2f), link.Y + (link.Height / 2f));
        Assert.True(view.IsClickable(center));
        view.MouseDown(center);

        Assert.IsType<DesktopCommand.OpenDeveloperPage>(Assert.Single(commands));
        Assert.Equal("https://5rs.uk/0", AppInfo.DeveloperPage);
    }

    [Fact]
    public void 行が少なくてもパネルの枠はウィンドウの下まで描き_行は上に寄せる()
    {
        var commands = new List<DesktopCommand>();
        using var view = Window(commands, DesktopView.DefaultClientSize);
        var full = view.PanelRect;

        view.SetRows(SampleRows.Build().Take(1).ToList());

        Assert.Equal(full.Height, view.PanelRect.Height, 1);
        var style = new PanelStyle();
        var scale = full.Width / style.Width;
        Assert.Equal(full.Y + (style.HeaderHeight * scale), view.RowScreenRect(0).Y, 1);
    }
}
