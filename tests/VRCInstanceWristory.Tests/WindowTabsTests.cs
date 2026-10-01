using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// デスクトップのウィンドウの右側をタブにして、選んだ行の詳しい情報を出す（2026-09-26のユーザー指定→実装メモ5.42）。
///
/// 一緒にいた人（→5.46）・写真（→5.47）・グループ名（→5.48）・「ここへ戻る」（→5.43）を出す場所として、
/// 「選んだ行」のタブを足した。設定は「パネルと目印」「記録と起動」の2つのタブに分けた。
/// パネルの行は左クリックで選び、右クリックでVR内と同じ目印のポップアップを開く。
/// </summary>
public class WindowTabsTests
{
    [Fact]
    public void 起動したときは記録と起動のタブで_タブを押すと切り替わる()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);

        Assert.Equal(DesktopTab.Startup, view.Tab);
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.StepperPlus, 0));
        Assert.Null(view.TargetRect(SettingsView.HitKind.StepperPlus, 1));

        // 「インスタンス操作」「グループ名」は一般設定のタブにある（→実装メモ5.71）。
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.ReturnAction, 0));
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.GroupIdWithName));

        SampleWindow.Click(view, view.TabRect(DesktopTab.Panel));
        Assert.Equal(DesktopTab.Panel, view.Tab);
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.VrOverlay));
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.StepperPlus, 1));
        Assert.Null(view.TargetRect(SettingsView.HitKind.ReturnAction, 0));
        Assert.Null(view.TargetRect(SettingsView.HitKind.StepperPlus, 0));

        SampleWindow.Click(view, view.TabRect(DesktopTab.Details));
        Assert.Equal(DesktopTab.Details, view.Tab);
        Assert.Null(view.TargetRect(SettingsView.HitKind.StepperPlus, 1));

        // タブの切り替えそのものは何も送らない。
        Assert.Empty(commands);
    }

    [Fact]
    public void 行をクリックするとその行を選んで選んだ行のタブを出す()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        int AccentAtLeftEdge()
        {
            // パネルの左端の細い帯には、選んだ行の枠のほかにアクセント色のものはない（▶ と棒はもっと内側）。
            using var bitmap = view.RenderToBitmap();
            var accent = new PanelStyle().Accent;
            var panel = view.PanelRect;
            var count = 0;

            for (var x = (int)panel.X; x < (int)panel.X + 6; x++)
            {
                for (var y = (int)panel.Y; y < (int)panel.Bottom; y++)
                {
                    var p = bitmap.GetPixel(x, y);

                    if (Math.Abs(p.R - accent.R) < 40 && Math.Abs(p.G - accent.G) < 40 && Math.Abs(p.B - accent.B) < 40)
                        count++;
                }
            }

            return count;
        }

        Assert.Equal(0, AccentAtLeftEdge());

        SampleWindow.Click(view, SampleWindow.PointOfRow(view, rows, "86688"));
        view.MouseLeave();

        Assert.Equal(DesktopTab.Details, view.Tab);
        Assert.Equal(rows.Single(r => r.InstanceId == "86688").EventId, view.SelectedEventId);
        Assert.False(view.MarkPopupOpen);
        Assert.Empty(commands);

        // 選んだ行はアクセント色の枠で囲む（ウィンドウだけ）。
        Assert.True(AccentAtLeftEdge() > 20, "選んだ行の枠が左端にあるはず");
    }

    [Fact]
    public void 選んだ行のページから目印を付け外しする()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        SampleWindow.Click(view, SampleWindow.PointOfRow(view, rows, "86688"));
        SampleWindow.Click(view, view.DetailsTargetRect(RowDetailsView.HitKind.Mark, 2)!.Value);

        var mark = Assert.IsType<DesktopCommand.SetMark>(Assert.Single(commands));
        Assert.Equal(InstanceMarks.Choices[2], mark.Mark);
        Assert.Equal(view.SelectedEventId, mark.EventId);
    }

    [Fact]
    public void 選んだ行が消えたら選んでいないページに戻る()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        SampleWindow.Click(view, SampleWindow.PointOfRow(view, rows, "86688"));
        Assert.NotNull(view.SelectedEventId);

        view.SetRows(rows.Where(r => r.InstanceId != "86688").ToList());

        Assert.Null(view.SelectedEventId);
        Assert.Null(view.DetailsTargetRect(RowDetailsView.HitKind.Return));
    }

    [Fact]
    public void ポップアップを開いている間の右クリックは何も変えずに閉じる()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();
        var point = SampleWindow.PointOfRow(view, rows, "24688");

        view.MouseMove(point);
        view.RightMouseDown(point);
        Assert.True(view.MarkPopupOpen);

        view.RightMouseDown(point);
        Assert.False(view.MarkPopupOpen);
        Assert.Empty(commands);
    }

    [Fact]
    public void ダッシュボードにはウィンドウだけのまとまりと起動と外部連携以外を出す()
    {
        using var dashboard = new SettingsDashboardView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });

        foreach (var kind in new[]
                 {
                     SettingsView.HitKind.StepperPlus,
                     SettingsView.HitKind.TargetType,
                     SettingsView.HitKind.WristSide,
                     SettingsView.HitKind.ReturnAction,
                     SettingsView.HitKind.GroupIdWithName,
                     SettingsView.HitKind.ResetPlacement,
                     SettingsView.HitKind.ResetWarning,
                     SettingsView.HitKind.TargetPause,
                 })
        {
            Assert.NotNull(dashboard.TargetRect(kind));
        }

        // 「起動」「外部連携」は 2026-09-29 のユーザー指定でダッシュボードから外した（→実装メモ5.90）。
        foreach (var kind in new[]
                 {
                     SettingsView.HitKind.TopMost,
                     SettingsView.HitKind.PhotoViewer,
                     SettingsView.HitKind.LaunchWithSteamVr,
                     SettingsView.HitKind.LaunchAtLogon,
                     SettingsView.HitKind.ExternalReset,
                     SettingsView.HitKind.CopyCommand,
                 })
        {
            Assert.Null(dashboard.TargetRect(kind));
        }
    }

    [Fact]
    public void 行のボタンで開くものを選ぶと変更を送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);

        // 一般設定の下のほうにあるので、送らずに全部見える高さにする。
        view.Resize(new Size(1060, 1400), 1f);
        view.SelectTab(DesktopTab.Startup);

        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.ReturnAction, 1)!.Value);

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(ReturnAction.VrChat, change.Settings.ReturnAction);
        Assert.Equal(SettingsField.ReturnAction, change.Fields);

        // いま選んでいるものを押し直しても送らない。
        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.ReturnAction, 1)!.Value);
        Assert.Single(commands);

        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.GroupIdWithName)!.Value);
        var group = Assert.IsType<DesktopCommand.ChangeSettings>(commands[^1]);
        Assert.True(group.Settings.ShowGroupIdWithName);
        Assert.Equal(SettingsField.GroupDisplay, group.Fields);
    }

    [Fact]
    public void 選んだ行のページは狭いウィンドウでも一緒にいた人の欄まで収まる()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        view.Resize(DesktopView.MinClientSize, 1f);
        SampleWindow.Click(view, SampleWindow.PointOfRow(view, rows, "86688"));

        // 写真（4枚＝2段）を並べても、下に一緒にいた人の欄が残る。
        var photos = view.DetailsTargetRect(RowDetailsView.HitKind.Photo, 3)!.Value;
        Assert.True(photos.Bottom < DesktopView.MinClientSize.Height - 60);

        using var bitmap = view.RenderToBitmap();
        Assert.Equal(DesktopView.MinClientSize.Width, bitmap.Width);
    }
}
