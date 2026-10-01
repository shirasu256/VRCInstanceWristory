using System.Drawing;
using System.Drawing.Imaging;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 撮った写真のサムネイルと、写真を開くアプリ（2026-09-27のユーザー指定→実装メモ5.55）。
///
/// サムネイルは専用のスレッドで作り、履歴から消えた写真のもの（ログのリセット）は消す。
/// サムネイルをダブルクリックすると（→5.65）、設定のアプリ（既定は Windows のフォト）で開く。検証では写真を実際には開かない。
/// </summary>
public class ThumbnailTests
{
    [Fact]
    public void サムネイルのファイル名は写真の場所から決まり_大文字小文字を区別しない()
    {
        var a = PhotoThumbnails.FileName(@"C:\Pictures\VRChat\a.png");

        Assert.Equal(a, PhotoThumbnails.FileName(@"c:\pictures\vrchat\A.PNG"));
        Assert.NotEqual(a, PhotoThumbnails.FileName(@"C:\Pictures\VRChat\b.png"));
        Assert.EndsWith(".jpg", a);
    }

    [Theory]
    [InlineData(7680, 4320, 320, 180)]
    [InlineData(1920, 1080, 320, 180)]
    [InlineData(1080, 1920, 101, 180)]
    [InlineData(200, 100, 200, 100)]
    public void サムネイルは縦横比を保って最大の大きさに収める(int width, int height, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), PhotoThumbnails.FitSize(width, height, PhotoThumbnails.MaxWidth, PhotoThumbnails.MaxHeight));

    [Fact]
    public void 写真からサムネイルを作って知らせ_履歴から消えた写真のものは消す()
    {
        using var temp = new TempFolder();
        var photo = temp.Photo("VRChat_1.png", 1600, 900, Color.OrangeRed);
        var other = temp.Photo("VRChat_2.png", 900, 1600, Color.SteelBlue);
        var directory = Path.Combine(temp.Path, "thumbnails");
        var ready = new List<string>();

        using var thumbnails = new PhotoThumbnails(directory, new CollectingDiagnostics());
        thumbnails.Ready += path => { lock (ready) ready.Add(path); };

        thumbnails.Sync([photo, other]);
        Assert.True(Eventually.True(() => File.Exists(thumbnails.PathFor(photo)) && File.Exists(thumbnails.PathFor(other))));
        Assert.True(thumbnails.WaitIdle(TimeSpan.FromSeconds(10)));

        lock (ready)
            Assert.Equal(2, ready.Count);

        using (var image = thumbnails.TryLoad(photo)!)
        {
            Assert.Equal(new Size(320, 180), image.Size);

            // 写真の色が出ている（真ん中の画素）。
            var center = image.GetPixel(160, 90);
            Assert.True(center.R > 200 && center.B < 80, $"{center}");
        }

        using (var portrait = thumbnails.TryLoad(other)!)
            Assert.Equal(180, portrait.Height);

        // 1枚目の訪問がログのリセットで消えた。そのサムネイルも消す。
        thumbnails.Sync([other]);
        Assert.True(Eventually.True(() => !File.Exists(thumbnails.PathFor(photo))));
        Assert.True(File.Exists(thumbnails.PathFor(other)));
        Assert.Null(thumbnails.TryLoad(photo));
    }

    [Fact]
    public void ウィンドウを出さないときは作らず_消すだけにする()
    {
        using var temp = new TempFolder();
        var photo = temp.Photo("VRChat_1.png", 640, 360, Color.Gold);
        var directory = Path.Combine(temp.Path, "thumbnails");
        Directory.CreateDirectory(directory);

        // 前の起動で作ったサムネイル（もう履歴にない写真のもの）。
        var stale = Path.Combine(directory, PhotoThumbnails.FileName(@"C:\old\photo.png"));
        File.WriteAllBytes(stale, [1, 2, 3]);

        using var thumbnails = new PhotoThumbnails(directory, new CollectingDiagnostics(), generate: false);
        thumbnails.Sync([photo]);

        Assert.True(Eventually.True(() => !File.Exists(stale)));
        Assert.True(thumbnails.WaitIdle(TimeSpan.FromSeconds(10)));
        Assert.False(File.Exists(thumbnails.PathFor(photo)));
    }

    [Fact]
    public void 見つからない写真や壊れた写真では作らず_落ちない()
    {
        using var temp = new TempFolder();
        var broken = Path.Combine(temp.Path, "broken.png");
        File.WriteAllBytes(broken, [0x89, 0x50, 0x4e, 0x47, 1, 2, 3]);
        var directory = Path.Combine(temp.Path, "thumbnails");
        var log = new CollectingDiagnostics();

        using var thumbnails = new PhotoThumbnails(directory, log);
        thumbnails.Sync([Path.Combine(temp.Path, "missing.png"), broken]);

        Assert.True(thumbnails.WaitIdle(TimeSpan.FromSeconds(10)));
        Assert.False(File.Exists(thumbnails.PathFor(broken)));
        Assert.DoesNotContain(log.Messages, m => m.StartsWith("ERROR"));
    }

    [Theory]
    [InlineData("Default", PhotoViewerKind.Default)]
    [InlineData("CUSTOM", PhotoViewerKind.Custom)]
    public void 写真を開くアプリの設定の名前を読む(string name, PhotoViewerKind expected)
        => Assert.Equal(expected, PhotoViewers.Parse(name));

    [Fact]
    public void 写真を開くアプリの既定は既定のアプリ_選んだアプリがなければ既定のアプリへ戻す()
    {
        Assert.Equal(PhotoViewerKind.Default, new AppSettings().Viewer);

        var log = new CollectingDiagnostics();
        var settings = new AppSettings { PhotoViewer = "custom", PhotoViewerPath = " " };
        settings.Validate(log);

        Assert.Equal(PhotoViewerKind.Default, settings.Viewer);
        Assert.Null(settings.PhotoViewerPath);
        Assert.Contains(log.Messages, m => m.Contains("photoViewer"));
    }

    [Fact]
    public void なくしたフォトの設定は警告なしで既定のアプリとして読む()
    {
        // 2026-09-27まで既定だった photos（→実装メモ5.58）。書いてある settings.json がそのまま残っている。
        var log = new CollectingDiagnostics();
        var settings = new AppSettings { PhotoViewer = "photos" };
        settings.Validate(log);

        Assert.Equal(PhotoViewerKind.Default, settings.Viewer);
        Assert.Equal("default", settings.PhotoViewer);
        Assert.DoesNotContain(log.Messages, m => m.Contains("photoViewer"));
    }

    [Fact]
    public void 選んだアプリを押すとファイルを選ぶ画面を頼み_選んだら変更を送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        Assert.Equal(DesktopTab.Startup, view.Tab);

        // 写真は一般設定の下のほう（→実装メモ5.71）。送らずに全部見える高さにする。
        view.Resize(new Size(1060, 1400), 1f);

        // 選択肢は「既定のアプリ」「選んだアプリ」の2つで、既定は「既定のアプリ」（→実装メモ5.58）。
        Assert.Null(view.TargetRect(SettingsView.HitKind.PhotoViewer, 2));
        Assert.Equal(PhotoViewerKind.Default, view.Settings.PhotoViewer);

        // 「選んだアプリ」は、選ぶ画面をウィンドウに頼むだけで、まだ何も送らない。
        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.PhotoViewer, 1)!.Value);
        Assert.Empty(commands);
        Assert.True(view.TakeAppChoiceRequest());
        Assert.False(view.TakeAppChoiceRequest());

        view.ChoosePhotoViewer(@"C:\Tools\viewer.exe");
        var chosen = Assert.IsType<DesktopCommand.ChangeSettings>(commands[^1]);
        Assert.Equal(PhotoViewerKind.Custom, chosen.Settings.PhotoViewer);
        Assert.Equal(@"C:\Tools\viewer.exe", chosen.Settings.PhotoViewerPath);

        // 選んだアプリの設定は書き戻せる。
        var settings = new AppSettings();
        chosen.Settings.ApplyTo(settings, chosen.Fields);
        Assert.Equal("custom", settings.PhotoViewer);
        Assert.Equal(@"C:\Tools\viewer.exe", settings.PhotoViewerPath);

        // 「既定のアプリ」はその場で変わる。
        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.PhotoViewer, 0)!.Value);
        var change = Assert.IsType<DesktopCommand.ChangeSettings>(commands[^1]);
        Assert.Equal(PhotoViewerKind.Default, change.Settings.PhotoViewer);
        Assert.Equal(SettingsField.PhotoViewer, change.Fields);
    }

    [Fact]
    public void サムネイルができていればその絵を描き_まだなら枠だけにする()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        using var red = new Bitmap(320, 180);
        using (var g = Graphics.FromImage(red))
            g.Clear(Color.Red);

        var point = SampleWindow.PointOfRow(view, rows, "86688");
        view.MouseMove(point);
        view.MouseDown(point);
        view.MouseUp();
        view.MouseLeave();

        var cell = view.DetailsTargetRect(RowDetailsView.HitKind.Photo, 0)!.Value;
        var center = new Point((int)(cell.X + (cell.Width / 2f)), (int)(cell.Y + (cell.Height / 2f)));

        using (var before = view.RenderToBitmap())
            Assert.True(before.GetPixel(center.X, center.Y).R < 150);

        // 1枚目だけサムネイルができた。
        view.Thumbnails = path => path.EndsWith("sample-1.png", StringComparison.OrdinalIgnoreCase) ? red : null;
        view.ThumbnailsChanged();

        using var after = view.RenderToBitmap();
        var pixel = after.GetPixel(center.X, center.Y);
        Assert.True(pixel.R > 200 && pixel.G < 60, $"{pixel}");

        var second = view.DetailsTargetRect(RowDetailsView.HitKind.Photo, 1)!.Value;
        Assert.True(after.GetPixel((int)(second.X + (second.Width / 2f)), (int)(second.Y + (second.Height / 2f))).R < 150);
    }

    [Fact]
    public void 入り切らない写真は最後の枠をほかn枚にして_押すとフォルダーを開く()
    {
        var now = new DateTime(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);
        var record = new VisitRecord
        {
            EventId = "e1",
            SourceSessionId = "s",
            SuccessByteOffset = 0,
            SessionOrder = 0,
            LocationKey = $"{Loc.WorldB}:55555~region(jp)",
            WorldId = Loc.WorldB,
            InstanceId = "55555",
            AccessType = AccessType.Public,
            WorldName = "写真の多いワールド",
            Location = Loc.Public("55555"),
            VisitedAtUtc = now.AddMinutes(-30),
            LeftAtUtc = now.AddMinutes(-5),
            VisitOrdinal = 1,
        };

        for (var i = 1; i <= 20; i++)
            record.Photos.Add(new VisitPhoto($@"C:\Pictures\p{i}.png", now.AddMinutes(-30 + i)));

        var commands = new List<DesktopCommand>();
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        var time = new LogTimeConverter(EngineHarness.Tokyo);
        var rows = RowFormatter.Build([record], null, time, now);

        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetRows(rows);
        view.SetDetails(RowDetails.Build([record], null));
        SampleWindow.Click(view, SampleWindow.PointOfRow(view, rows, "55555"));

        // 3列×3段＝9枠のうち8枚を出し、最後の枠は「ほか12枚」。
        Assert.NotNull(view.DetailsTargetRect(RowDetailsView.HitKind.Photo, 7));
        Assert.Null(view.DetailsTargetRect(RowDetailsView.HitKind.Photo, 8));

        var more = view.DetailsTargetRect(RowDetailsView.HitKind.OpenPhotos, 12)!.Value;
        SampleWindow.Click(view, more);

        var open = Assert.IsType<DesktopCommand.OpenPhotoFolder>(Assert.Single(commands));
        Assert.EndsWith("p20.png", open.Path);
    }

    /// <summary>検証用の一時フォルダー。写真の代わりの PNG を置ける。</summary>
    private sealed class TempFolder : IDisposable
    {
        private readonly TempDirectory _directory = new("thumbs");

        public string Path => _directory.Path;

        public string Photo(string name, int width, int height, Color color)
        {
            var path = System.IO.Path.Combine(Path, name);

            using var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(color);

            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        public void Dispose() => _directory.Dispose();
    }
}
