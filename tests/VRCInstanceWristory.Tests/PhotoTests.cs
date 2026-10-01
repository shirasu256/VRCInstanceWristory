using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Logging;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 撮った写真をその訪問へ紐付ける（2026-09-26のユーザー指定→実装メモ5.47）。
///
/// ログの <c>[VRC Camera] Took screenshot to: …png</c> を拾い、滞在中の訪問の行に写真の印と枚数を出す。
/// ウィンドウの「選んだ行」から、その写真のフォルダーを開ける（ここでは開く命令を送るところまで）。
/// </summary>
public class PhotoTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 26, 21, 0, 0);

    private const string PhotoPath = "H:/Pictures/VRChat\\2026-09\\VRChat_2026-09-26_21-02-00.000_1920x1080.png";

    [Fact]
    public void 写真の行を読んで保存先の区切りをそろえる()
    {
        var ev = Assert.Single(LogText.Events(LogText.Screenshot(SessionStart, PhotoPath)));

        Assert.Equal(LogEventKind.Screenshot, ev.Kind);
        Assert.Equal(PhotoPath, ev.Payload);
        Assert.Equal(@"H:\Pictures\VRChat\2026-09\VRChat_2026-09-26_21-02-00.000_1920x1080.png", VisitTracker.NormalizePhotoPath(ev.Payload));
    }

    [Fact]
    public void 似た行には反応しない()
    {
        var ev = Assert.Single(LogText.Events(LogText.Line(SessionStart, "[VRC Camera] Took screenshot failed")));
        Assert.Equal(LogEventKind.Other, ev.Kind);
    }

    [Fact]
    public void 滞在中に撮った写真だけをその訪問へ紐付ける()
    {
        using var dir = new TempLogDirectory();
        var t = SessionStart.AddMinutes(1);

        dir.WriteSession(
            SessionStart,
            LogText.Visit(t, Loc.GroupPublic("111"), "A")
            + LogText.Screenshot(t.AddMinutes(1), PhotoPath)
            + LogText.Screenshot(t.AddMinutes(1), PhotoPath)
            + LogText.Screenshot(t.AddMinutes(2), PhotoPath.Replace("00.000", "30.000"))

            // 対象外（Friends）にいる間と、移動の途中に撮った写真は、どの行にも付けない。
            + LogText.Move(t.AddMinutes(4), Loc.Friends("999"), "F")
            + LogText.Screenshot(t.AddMinutes(5), PhotoPath.Replace("00.000", "45.000"))
            + LogText.DestinationSet(t.AddMinutes(6), Loc.Public("222"))
            + LogText.Screenshot(t.AddMinutes(6), PhotoPath.Replace("00.000", "50.000")));

        using var harness = new EngineHarness(dir.Path, t.AddMinutes(7), Process(SessionStart));
        harness.Engine.Initialize();

        var visit = Assert.Single(harness.Snapshot().History);

        // 同じ保存先は二重に数えない。
        Assert.Equal(2, visit.Photos.Count);
        Assert.Equal(harness.Utc(t.AddMinutes(1)), visit.Photos[0].TakenAtUtc);

        var row = RowFormatter.Build(visit, harness.Time, isCurrent: false, harness.Clock.UtcNow);
        Assert.Equal(2, row.PhotoCount);

        var detail = Assert.Single(RowDetails.Build(harness.Snapshot().History, null));
        Assert.Equal(visit.Photos[1], detail.LatestPhoto);
    }

    [Fact]
    public void 写真の枚数が変われば別の行の内容として描き直す()
    {
        var row = new DisplayRow("a", "111", "01:00 - 01:10", "1回目", "世界", "Public", false);
        Assert.NotEqual(row, row with { PhotoCount = 1 });
    }

    [Fact]
    public void 写真のある行だけ1段目の右端に写真の印を描く()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var plain = new DisplayRow("a", "111", "01:00 - 01:10", "1回目", "世界", "Public", false);
        var layouts = renderer.Measure([plain, plain with { EventId = "b", PhotoCount = 3 }]);
        renderer.RenderRows(layouts);

        var pixels = renderer.RowsPixels();
        var withPhoto = renderer.PhotoCountRect(3, layouts[0].Height, layouts[1].Height);
        var withoutPhoto = withPhoto with { Y = withPhoto.Y - layouts[0].Height };

        static int Changed(byte[] pixels, int width, RectangleF rect, Color background)
        {
            var count = 0;

            for (var y = (int)rect.Top; y < (int)rect.Bottom; y++)
            {
                for (var x = (int)rect.Left; x < (int)rect.Right; x++)
                {
                    var i = ((y * width) + x) * 4;

                    if (Math.Abs(pixels[i + 2] - background.R) > 40)
                        count++;
                }
            }

            return count;
        }

        Assert.True(Changed(pixels, style.Width, withPhoto, style.Surface) > 30, "写真の印と枚数が描かれているはず");
        Assert.Equal(0, Changed(pixels, style.Width, withoutPhoto, style.Surface));

        // 付加情報の列の中に収まる。
        Assert.True(withPhoto.Right <= style.AuxColumnLeft + style.AuxColumnWidth + 0.5f);
    }

    [Fact]
    public void 選んだ行のサムネイルをダブルクリックするとその写真を開き_見出しの右からフォルダーを開く()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        // 見本の 86688 には写真が4枚ある（→5.55）。
        var point = SampleWindow.PointOfRow(view, rows, "86688");
        view.MouseMove(point);
        view.MouseDown(point);
        view.MouseUp();

        // 2枚目のサムネイル。1回押しただけでは開かず、ダブルクリックで開く（→5.65）。
        var second = view.DetailsTargetRect(RowDetailsView.HitKind.Photo, 1)!.Value;
        view.MouseMove(SampleWindow.Center(second));
        view.MouseDown(SampleWindow.Center(second));
        view.MouseUp();
        Assert.Empty(commands);

        view.MouseDown(SampleWindow.Center(second));
        view.MouseDoubleClick(SampleWindow.Center(second));
        view.MouseUp();

        var open = Assert.IsType<DesktopCommand.OpenPhoto>(Assert.Single(commands));
        Assert.EndsWith("sample-2.png", open.Path);

        // 5枚とも並ぶ（3列・2段）。
        Assert.NotNull(view.DetailsTargetRect(RowDetailsView.HitKind.Photo, 3));
        Assert.True(view.DetailsTargetRect(RowDetailsView.HitKind.Photo, 3)!.Value.Y > second.Bottom);

        // 見出しの右の「フォルダーを開く」は、いちばん新しい写真を選んで開く。
        var folder = view.DetailsTargetRect(RowDetailsView.HitKind.OpenPhotos, -1)!.Value;
        view.MouseMove(SampleWindow.Center(folder));
        view.MouseDown(SampleWindow.Center(folder));

        var folderCommand = Assert.IsType<DesktopCommand.OpenPhotoFolder>(commands[^1]);
        Assert.EndsWith("sample-5.png", folderCommand.Path);
    }

    [Fact]
    public void 写真のない行にはサムネイルもフォルダーを開くボタンも出さない()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        var point = SampleWindow.PointOfRow(view, rows, "db7da28295");
        view.MouseMove(point);
        view.MouseDown(point);
        view.MouseUp();

        Assert.Equal(DesktopTab.Details, view.Tab);
        Assert.Null(view.DetailsTargetRect(RowDetailsView.HitKind.Photo, 0));
        Assert.Null(view.DetailsTargetRect(RowDetailsView.HitKind.OpenPhotos, -1));
        Assert.Empty(commands);
    }
}
