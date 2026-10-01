using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 行からそのインスタンスを開く（2026-09-26のユーザー指定→実装メモ5.43・2026-09-27に5.53で既定をブラウザへ）。
///
/// VRChat API は使わず、ログの location から URL を作るだけ。既定は vrchat.com のインスタンスのページ（「ブラウザで開く」）、
/// 設定 <c>returnAction</c> を <c>vrchat</c> にすると <c>vrchat://launch</c>（「ここへ戻る」。VRChat はクライアントを起動し直す）。
/// ここでは URL を作るところと命令を送るところまでを確かめ、実際に開くことはしない（開くと VRChat が起動し直してしまう）。
/// </summary>
public class InstanceLaunchTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 26, 21, 0, 0);

    [Fact]
    public void locationからWebサイトと同じ形のlaunch_URLを作る()
    {
        var location = Loc.GroupPublic("07254");

        Assert.Equal($"vrchat://launch?ref=vrchat.com&id={location}", InstanceLaunch.UrlFor(location));
        Assert.Equal($"vrchat://launch?ref=vrchat.com&id={Loc.Public("12345")}", InstanceLaunch.UrlFor(Loc.Public("12345")));
    }

    [Fact]
    public void locationからvrchat_comの共有リンクと同じ形のWebページのURLを作る()
    {
        var location = Loc.GroupPublic("07254");
        var instance = location[(location.IndexOf(':') + 1)..];

        Assert.Equal(
            $"https://vrchat.com/home/launch?worldId={Loc.WorldA}&instanceId={instance}",
            InstanceLaunch.WebUrlFor(location));

        // Webサイトの共有リンクと同じく、タグ（括弧など）はそのまま入れる（区切りになる文字は入れる前に弾いてある）。
        Assert.Equal($"https://vrchat.com/home/launch?worldId={Loc.WorldB}&instanceId=12345~region(jp)", InstanceLaunch.WebUrlFor(Loc.Public("12345")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a location")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000005:07254~nonce(a&b=c)")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000005:07254~region(jp) --flag")]
    [InlineData("wrld_00000000-0000-4000-9000-000000000005:07254~region(jp)#x")]
    public void 解析できない_URLの区切りになる文字を含むlocationは開かない(string? location)
    {
        Assert.Null(InstanceLaunch.UrlFor(location));
        Assert.Null(InstanceLaunch.WebUrlFor(location));
        Assert.False(InstanceLaunch.CanOpen(location));
    }

    [Theory]
    [InlineData("browser", ReturnAction.Browser)]
    [InlineData("VRChat", ReturnAction.VrChat)]
    [InlineData(" vrchat ", ReturnAction.VrChat)]
    public void 開き方の設定の名前を読む(string name, ReturnAction expected)
        => Assert.Equal(expected, ReturnActions.Parse(name));

    [Fact]
    public void 開き方の既定はブラウザ_知らない名前は知らせて既定へ戻す()
    {
        Assert.Equal(ReturnAction.Browser, new AppSettings().OpenAction);
        Assert.Equal("ブラウザで開く", ReturnActions.ButtonLabel(ReturnAction.Browser));
        Assert.Equal("ここへ戻る", ReturnActions.ButtonLabel(ReturnAction.VrChat));

        var log = new CollectingDiagnostics();
        var settings = new AppSettings { ReturnAction = "launch" };
        settings.Validate(log);

        Assert.Equal("browser", settings.ReturnAction);
        Assert.Contains(log.Messages, m => m.Contains("returnAction"));
    }

    [Fact]
    public void 訪問の記録はログに書かれたlocationをそのまま持つ()
    {
        using var dir = new TempLogDirectory();

        // タグの並びは locationKey（並べ替えた比較用の値）と違ってもよい。ログのまま渡す。
        const string location = "wrld_00000000-0000-4000-9000-000000000005:07254~region(jp)~group(grp_00000001-0000-4000-a000-000000000000)~groupAccessType(public)";
        dir.WriteSession(SessionStart, LogText.Visit(SessionStart.AddMinutes(1), location, "A"));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        var visit = Assert.Single(harness.Snapshot().History);
        Assert.Equal(location, visit.Location);
        Assert.NotEqual(location, visit.LocationKey);
        Assert.Same(visit, harness.Engine.FindVisit(visit.EventId));
    }

    [Fact]
    public void いま滞在しているインスタンスの行ではここへ戻るを押せない()
    {
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        var records = new List<VisitRecord>
        {
            Record("a", "111", now.AddMinutes(-30), left: true),
            Record("b", "222", now.AddMinutes(-20), left: true),
            Record("c", "111", now.AddMinutes(-10), left: false),
        };

        var rows = RowFormatter.Build(records, "c", LogTimeConverter.Local, now);

        // 111 には今いるので、前に訪れた行（a）も押せない。222 へは戻れる。
        Assert.False(rows[0].Returnable);
        Assert.True(rows[1].Returnable);
        Assert.False(rows[2].Returnable);

        // 滞在していなければ、どの行からも戻れる。
        Assert.All(RowFormatter.Build(records, null, LogTimeConverter.Local, now), r => Assert.True(r.Returnable));

        // location が残っていない行（古いデータ）では押せない。
        var unknown = Record("d", "333", now, left: true, location: null);
        Assert.False(RowFormatter.Build(unknown, LogTimeConverter.Local, isCurrent: false, now).Returnable);
        Assert.False(RowFormatter.Build(unknown, LogTimeConverter.Local, isCurrent: false, now).Linkable);
    }

    /// <summary>ページを見るだけで VRChat には何も起きないので、いま滞在しているインスタンスでも押せる（→5.53）。</summary>
    [Fact]
    public void ブラウザで開くはいま滞在しているインスタンスの行でも押せる()
    {
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        var records = new List<VisitRecord>
        {
            Record("a", "111", now.AddMinutes(-30), left: true),
            Record("c", "111", now.AddMinutes(-10), left: false),
        };

        var rows = RowFormatter.Build(records, "c", LogTimeConverter.Local, now);

        Assert.All(rows, r => Assert.True(r.CanOpen(ReturnAction.Browser)));
        Assert.All(rows, r => Assert.False(r.CanOpen(ReturnAction.VrChat)));

        var details = RowDetails.Build(records, "c");
        Assert.All(details, d => Assert.True(d.CanOpen(ReturnAction.Browser)));
        Assert.All(details, d => Assert.False(d.CanOpen(ReturnAction.VrChat)));
    }

    [Fact]
    public void 詳しい情報にもlaunch_URLと滞在中かを入れる()
    {
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        var records = new List<VisitRecord>
        {
            Record("a", "111", now.AddMinutes(-30), left: true),
            Record("b", "222", now.AddMinutes(-10), left: false),
        };

        var details = RowDetails.Build(records, "b");

        Assert.Equal(InstanceLaunch.UrlFor(records[0].Location), details[0].LaunchUrl);
        Assert.Equal(InstanceLaunch.WebUrlFor(records[0].Location), details[0].WebUrl);
        Assert.False(details[0].HereNow);
        Assert.True(details[1].HereNow);
    }

    [Fact]
    public void ポップアップでここへ戻るを指せる_押せない行では指さない()
    {
        var style = new PanelStyle();
        var row = PanelGeometry.RowHighlightRect(style, style.MaxViewportHeight, 0f, style.RowHeight, style.RowHeight);
        var popup = PanelGeometry.MarkPopupRect(style, style.MaxViewportHeight, row, 3);
        var back = PanelGeometry.ReturnChoiceRect(popup);

        Assert.Equal(PanelGeometry.ReturnChoiceIndex, PanelGeometry.PopupItemAt(popup, SampleWindow.Center(back), 3));
        Assert.Equal(1, PanelGeometry.PopupItemAt(popup, SampleWindow.Center(PanelGeometry.MarkChoiceRect(popup, 1)), 3));
        Assert.Equal(-1, PanelGeometry.PopupItemAt(popup, new PointF(back.X - (PanelGeometry.ReturnChoiceGap / 2f), back.Y + 10f), 3));

        // どの選択肢とも重ならず、ポップアップの中に収まる。
        Assert.True(popup.Contains(back));
        Assert.False(back.IntersectsWith(PanelGeometry.MarkChoiceRect(popup, 2)));
    }

    [Fact]
    public void ポップアップのここへ戻るは押せるときだけ文字が明るい()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);
        var layouts = renderer.Measure(SampleRows.Build());
        renderer.RenderRows(layouts);

        var row = PanelGeometry.RowHighlightRect(style, renderer.ViewportHeight, 0f, 0f, style.RowHeight);
        var popup = PanelGeometry.MarkPopupRect(style, renderer.ViewportHeight, row, 3);
        var back = PanelGeometry.ReturnChoiceRect(popup);

        var enabled = PanelPixels.Compose(renderer, 0f, new PanelDecorations { Popup = popup, PopupReturnEnabled = true });
        var disabled = PanelPixels.Compose(renderer, 0f, new PanelDecorations { Popup = popup, PopupReturnEnabled = false });

        static int Brightest(byte[] pixels) => Enumerable.Range(0, pixels.Length / 4).Max(i => pixels[(i * 4) + 1]);

        var bright = Brightest(PanelPixels.Crop(enabled, style.Width, back));
        var dim = Brightest(PanelPixels.Crop(disabled, style.Width, back));

        Assert.True(bright > 200, $"押せるときは明るい文字（{bright}）");
        Assert.True(dim < bright - 60, $"押せないときは控えめな文字（{dim}）");
    }

    [Fact]
    public void ウィンドウでは右クリックのポップアップから開く命令を送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);

        // 4行目（39437）は滞在中ではないので戻れる。見えている行の中ほどを右クリックする。
        var rows = SampleRows.Build();
        var point = SampleWindow.PointOfRow(view, rows, "24688");

        view.MouseMove(point);
        view.RightMouseDown(point);
        Assert.True(view.MarkPopupOpen);

        var back = view.ReturnChoiceRect()!.Value;
        view.MouseMove(SampleWindow.Center(back));
        view.MouseDown(SampleWindow.Center(back));

        var launch = Assert.IsType<DesktopCommand.OpenInstance>(Assert.Single(commands));
        Assert.Equal(rows.Single(r => r.InstanceId == "24688").EventId, launch.EventId);
        Assert.False(view.MarkPopupOpen);
    }

    [Fact]
    public void ウィンドウでも滞在中の行のここへ戻るは押せない()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands, new AppSettings { ReturnAction = "vrchat" });

        var panel = view.PanelRect;
        var scale = panel.Width / new PanelStyle().Width;
        var last = new PointF(panel.X + (400f * scale), panel.Bottom - ((new PanelStyle().FooterHeight + 56f) * scale));

        view.MouseMove(last);
        view.RightMouseDown(last);
        var back = view.ReturnChoiceRect()!.Value;
        view.MouseMove(SampleWindow.Center(back));
        view.MouseDown(SampleWindow.Center(back));

        Assert.Empty(commands);
        Assert.False(view.MarkPopupOpen);
    }

    [Fact]
    public void 既定のブラウザで開くは滞在中の行からも送り_設定を変えるとその場で押せなくなる()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        Assert.Equal(ReturnAction.Browser, view.ReturnAction);

        var panel = view.PanelRect;
        var scale = panel.Width / new PanelStyle().Width;
        var last = new PointF(panel.X + (400f * scale), panel.Bottom - ((new PanelStyle().FooterHeight + 56f) * scale));

        view.MouseMove(last);
        view.RightMouseDown(last);
        var back = view.ReturnChoiceRect()!.Value;
        view.MouseMove(SampleWindow.Center(back));
        view.MouseDown(SampleWindow.Center(back));

        Assert.IsType<DesktopCommand.OpenInstance>(Assert.Single(commands));

        // ダッシュボードで「VRChatクライアントを再起動してjoin」へ変えた。滞在中の行の「ここへ戻る」は押せない。
        view.SetSettings(view.Settings with { ReturnAction = ReturnAction.VrChat });
        Assert.Equal(ReturnAction.VrChat, view.ReturnAction);

        view.MouseMove(last);
        view.RightMouseDown(last);
        view.MouseMove(SampleWindow.Center(view.ReturnChoiceRect()!.Value));
        view.MouseDown(SampleWindow.Center(view.ReturnChoiceRect()!.Value));

        Assert.Single(commands);
    }

    [Fact]
    public void 選んだ行のページのここへ戻るも同じ命令を送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();

        var point = SampleWindow.PointOfRow(view, rows, "24688");
        view.MouseMove(point);
        view.MouseDown(point);
        view.MouseUp();

        Assert.Equal(DesktopTab.Details, view.Tab);

        var button = view.DetailsTargetRect(RowDetailsView.HitKind.Return)!.Value;
        view.MouseMove(SampleWindow.Center(button));
        view.MouseDown(SampleWindow.Center(button));

        Assert.IsType<DesktopCommand.OpenInstance>(Assert.Single(commands));
    }

    // ------------------------------------------------------------------ 共通

    private static VisitRecord Record(string eventId, string instanceId, DateTime visitedUtc, bool left, string? location = "")
        => new()
        {
            EventId = eventId,
            SourceSessionId = "s",
            SuccessByteOffset = 0,
            SessionOrder = 0,
            LocationKey = $"{Loc.WorldB}:{instanceId}~region(jp)",
            WorldId = Loc.WorldB,
            InstanceId = instanceId,
            AccessType = AccessType.Public,
            WorldName = "世界",
            Location = location == string.Empty ? Loc.Public(instanceId) : location,
            VisitedAtUtc = visitedUtc,
            LeftAtUtc = left ? visitedUtc.AddMinutes(5) : null,
            VisitOrdinal = 1,
        };
}
