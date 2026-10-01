using System.Drawing;
using VRCInstanceWristory.Cli;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Scrolling;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// テクスチャ生成だけをPC側で確認する補助モード。
/// SteamVRを使わずに、日本語・等幅英数字の描画と7行のレイアウトを画像として確認できる。
/// </summary>
public static class RenderSampleMode
{
    /// <summary><paramref name="options"/> の描く対象を、<paramref name="outputPath"/> へ PNG で保存する。</summary>
    public static int Run(RenderSampleOptions options, string outputPath) => options.Target switch
    {
        RenderSampleTarget.Window => RunWindow(outputPath, options.MarkPopup, options.Tab, options.ResetConfirm, options.VrError),
        RenderSampleTarget.Dashboard => RunDashboard(outputPath),
        RenderSampleTarget.ResetWarning => RunResetWarning(outputPath),
        RenderSampleTarget.Welcome => RunWelcome(outputPath, options.Setup),
        RenderSampleTarget.UpdateConfirm => UpdateConfirmSample.Run(outputPath),
        _ => RunPanel(outputPath, options.ScrollOffset, options.MarkPopup, options.ResetConfirm),
    };

    /// <summary>手首のパネルの見本。</summary>
    public static int RunPanel(string outputPath, float scrollOffset, bool markPopup = false, bool clearConfirm = false)
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var time = LogTimeConverter.Local;
        var rows = SampleHistory.BuildRows(DateTime.UtcNow, time);
        var layouts = renderer.Measure(rows);

        // 実機と同じく、行が7.5行ぶんに満たないときはその行のぶんまで縦幅を詰める。
        renderer.SetViewportForContent(layouts.Sum(l => l.Height));

        var scroll = new ScrollController
        {
            RowHeight = style.RowHeight,
            ViewportHeight = renderer.ViewportHeight,
        };

        scroll.SetRows(renderer.ToScrollRows(layouts));
        scroll.ResetToTail();

        if (scrollOffset >= 0f)
        {
            // ResetToTail の直後は「一度ニュートラルへ戻るまで受け付けない」状態なので、
            // 中立の1フレームを挟んでから倒す（実機でUIに命中し始めた瞬間と同じ扱い）。
            // 軸は上を正とするので、末尾（最新）から古い側へ戻すには +1 を渡す。
            // この2点を誤っていたため、--scroll を渡しても末尾のままだった（2026-09-21に気づいた）。
            scroll.Update(0f, active: true, deltaSeconds: 0f);
            scroll.Update(1f, active: true, deltaSeconds: scrollOffset / (style.RowHeight * scroll.RowsPerSecond));
        }

        // 実機とまったく同じ経路で作る（行の下絵を描いてから1枚に組み立てる）。
        var rowsHeight = renderer.RenderRows(layouts);

        var decorations = new PanelDecorations
        {
            Countdown = Core.Presentation.Countdown.Format(TimeSpan.FromSeconds(3552)),
        };

        Console.WriteLine($"写真のある行 : {string.Join(", ", rows.Where(r => r.PhotoCount > 0).Select(r => $"{r.InstanceId}({r.PhotoCount}枚)"))}");

        if (markPopup)
            decorations = WithMarkPopup(decorations, renderer, layouts, scroll.Offset);

        // 見出しの「リセット」を押したあとの確認（→実装メモ5.65）。「リセット」を指している状態にする。
        if (clearConfirm)
            decorations = decorations with { ConfirmClear = true, ConfirmPointed = PanelGeometry.ConfirmAccept };

        renderer.Compose(scroll.Offset, decorations);

        var full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        renderer.SavePng(full);
        Console.WriteLine($"保存先       : {full}（{style.Width}×{renderer.Height}px）");
        Console.WriteLine($"行の下絵     : {style.Width}×{rowsHeight}px（スクロールはここから窓を写すだけで行う）");

        Console.WriteLine($"テクスチャ   : {style.Width}×{renderer.Height}px（上限 {style.Width}×{style.MaxHeight}px）");
        Console.WriteLine($"行高/表示領域: {style.RowHeight}px / {renderer.ViewportHeight}px（上限 {style.MaxViewportHeight}px＝{style.VisibleRows}行ぶん・完全表示 {style.FullyVisibleRows}行）");
        Console.WriteLine($"等幅フォント : {renderer.Fonts.MonospaceFamilyName}");
        Console.WriteLine($"日本語フォント: {renderer.Fonts.JapaneseFamilyName}");
        Console.WriteLine($"行数/内容高  : {layouts.Count}行 / {scroll.ContentHeight}px（scrollOffset={scroll.Offset:0.0}）");

        return ExitCode.Success;
    }

    /// <summary>
    /// デスクトップのウィンドウ全体の見本（→実装メモ5.39・5.42）。
    /// 実際のウィンドウと同じ <see cref="DesktopView"/> で、既定の大きさ・等倍（96dpi）の絵を保存する。
    /// 見本の8行目（86688・一緒にいた人と写真がある行）をクリックして、「選んだ行」を出した状態にする。
    /// <paramref name="markPopup"/> なら、いちばん下の行を右クリックして目印の選択肢を開いた状態にする。
    /// <paramref name="tab"/> を渡すと、右側をそのタブにする（設定のタブの見た目を確かめる）。
    /// <paramref name="clearConfirm"/> なら、見出しの「リセット」を押して確認を出し、「キャンセル」を指した状態にする（→実装メモ5.65）。
    /// </summary>
    /// <param name="vrError">SteamVR へつなげない状態を描き、その段を指して詳しい文を出す（<c>--vr-error</c>→実装メモ5.82・5.85）。</param>
    public static int RunWindow(string outputPath, bool markPopup = false, DesktopTab? tab = null, bool clearConfirm = false, bool vrError = false)
    {
        var settings = new AppSettings();
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(settings) with { LaunchWithSteamVr = false }, _ => { });

        // 状態の段の点は点滅するので（→実装メモ5.91）、見本では点いた状態で描く。
        view.BlinkClock = static () => TimeSpan.Zero;

        // 見本の写真は実在しないので、サムネイルは見本用の絵を使う（→実装メモ5.55）。
        var thumbnails = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        view.Thumbnails = path => thumbnails.TryGetValue(path, out var image) ? image : thumbnails[path] = SampleHistory.SampleThumbnail(path);

        var now = DateTime.UtcNow;
        var records = SampleHistory.Build(now);

        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetRows(SampleHistory.BuildRows(now, LogTimeConverter.Local));
        view.SetDetails(RowDetails.Build(records, records[^1].EventId, SampleHistory.Groups));
        view.SetCountdown(Core.Presentation.Countdown.Format(TimeSpan.FromSeconds(3552)));
        view.SetStatus(new DesktopStatus
        {
            ClientRunning = true,
            Health = LogHealth.Ok,
            VrConnected = !vrError,
            MenuPageOpen = false,
            VrHideReason = PanelHideReason.ContentNotReady,
            VrError = vrError ? VrConnectError.TextureDeviceExhausted : VrConnectError.None,
            ClientCount = 1,
            VrServerSeen = true,
        });

        var panel = view.PanelRect;
        var scale = panel.Width / new PanelStyle().Width;
        var style = new PanelStyle();

        // 見えている最後の行（滞在中の行）と、その1つ上の行（86688）の中ほど。
        var last = new PointF(panel.X + (panel.Width * 0.4f), panel.Bottom - ((style.FooterHeight + 60f) * scale));
        var selected = new PointF(last.X, last.Y - (style.RowHeight * scale));

        view.MouseMove(selected);
        view.MouseDown(selected);
        view.MouseUp();
        view.MouseLeave();

        if (markPopup)
        {
            view.MouseMove(last);
            view.RightMouseDown(last);

            // 右端のボタンを指している状態にする（既定の「ブラウザで開く」は滞在中の行でも押せる→実装メモ5.53）。
            if (view.ReturnChoiceRect() is { } back)
                view.MouseMove(new PointF(back.X + (back.Width / 2f), back.Y + (back.Height / 2f)));
        }

        if (clearConfirm)
        {
            var clear = view.ClearButtonScreenRect();
            view.MouseDown(new PointF(clear.X + (clear.Width / 2f), clear.Y + (clear.Height / 2f)));
            view.MouseUp();

            if (view.ConfirmButtonRect(PanelGeometry.ConfirmCancel) is { } cancel)
                view.MouseMove(new PointF(cancel.X + (cancel.Width / 2f), cancel.Y + (cancel.Height / 2f)));
        }

        if (tab is { } chosen)
            view.SelectTab(chosen);

        // SteamVR の段を指して、詳しい文を出した状態にする（→実装メモ5.85）。
        if (vrError)
        {
            var slot = view.StatusSlot(2);
            view.MouseMove(new PointF(slot.X + 40f, slot.Y + (slot.Height / 2f)));
        }

        using var bitmap = view.RenderToBitmap();

        foreach (var image in thumbnails.Values)
            image.Dispose();

        var full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        bitmap.Save(full, System.Drawing.Imaging.ImageFormat.Png);

        Console.WriteLine($"保存先       : {full}（{bitmap.Width}×{bitmap.Height}px・デスクトップのウィンドウ）");
        Console.WriteLine($"パネルの縮尺 : {view.PanelRect.Width / new PanelStyle().Width:0.000}");
        return ExitCode.Success;
    }

    /// <summary>
    /// SteamVRのダッシュボードに出す設定の画面の見本（→実装メモ5.40）。
    /// 実際と同じ <see cref="SettingsDashboardView"/> で描き、同じ場所に下のバーのアイコン（<c>*-icon.png</c>）も保存する。
    /// SteamVRには触れない。
    /// </summary>
    public static int RunDashboard(string outputPath)
    {
        // ダッシュボードはSteamVRにつながっているときにしか出ないので、「SteamVRと一緒に起動する」も押せる状態で描く。
        using var view = new SettingsDashboardView(new PanelStyle(), DesktopSettings.From(new AppSettings()) with { LaunchWithSteamVr = false }, _ => { });

        // レーザーで「＋」を指している状態にして、指し示しの見た目も確かめられるようにする。
        if (view.TargetRect(SettingsView.HitKind.StepperPlus, 1) is { } plus)
            view.PointerMove(new PointF(plus.X + (plus.Width / 2f), plus.Y + (plus.Height / 2f)));

        view.Render();

        var full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        view.Bitmap.Save(full, System.Drawing.Imaging.ImageFormat.Png);

        var icon = Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + "-icon.png");

        using (var thumbnail = SettingsDashboardView.RenderThumbnail(new PanelStyle(), 128))
            thumbnail.Save(icon, System.Drawing.Imaging.ImageFormat.Png);

        Console.WriteLine($"保存先       : {full}（{view.Width}×{view.Height}px・SteamVRのダッシュボード）");
        Console.WriteLine($"アイコン     : {icon}（128×128px）");
        Console.WriteLine($"設定の画面   : {view.Content.Width}×{view.Content.Height}px（周りは透明。枠の中で面積 {SettingsDashboardView.AreaRatio:P0}→実装メモ5.107）");
        Console.WriteLine($"目立たせる倍率: {view.EmphasisScale:0.00}倍（リセットまでの時間・インスタンスタイプ→実装メモ5.106）");
        return ExitCode.Success;
    }

    /// <summary>
    /// 初回起動の案内の画面の見本。実際のウィンドウと同じ <see cref="DesktopView"/> で、既定の大きさ・等倍の絵を保存する。
    /// <paramref name="setup"/> なら「わかった」を押して初期設定の画面（→実装メモ5.98）を出し、「はじめる」を指した状態にする。
    /// SteamVR にはつながっていない扱いなので、「SteamVR の開始時に自動起動する」は押せない見た目になる。
    /// </summary>
    public static int RunWelcome(string outputPath, bool setup = false)
    {
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.ShowWelcome();

        var button = view.WelcomeButtonRect();

        if (setup)
        {
            view.MouseDown(new PointF(button.X + (button.Width / 2f), button.Y + (button.Height / 2f)));
            view.MouseUp();
            button = view.SetupButtonRect();
        }

        view.MouseMove(new PointF(button.X + (button.Width / 2f), button.Y + (button.Height / 2f)));

        using var bitmap = view.RenderToBitmap();

        var full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        bitmap.Save(full, System.Drawing.Imaging.ImageFormat.Png);

        Console.WriteLine($"保存先       : {full}（{bitmap.Width}×{bitmap.Height}px・{(setup ? "初期設定" : "初回起動の案内")}）");
        return ExitCode.Success;
    }

    /// <summary>
    /// 履歴リセットの予告のアイコンの見本（→実装メモ5.87・5.89）。左から、暗い景色・明るい景色の上のアイコンと、
    /// マイクのアイコン（の代わりの灰色の形）に対する8つの表示位置（既定の左下を濃く、ほかを薄く）。
    /// 同じ場所にアイコンの絵そのもの（<c>*-texture.png</c>・透明の背景）も保存する。SteamVRには触れない。
    /// </summary>
    public static int RunResetWarning(string outputPath)
    {
        const int size = ResetWarningOverlay.TextureSize;
        const int margin = 24;
        const int tile = size + (margin * 2);
        const int layout = tile * 2;

        using var icon = ResetWarningOverlay.Render(size);
        using var sheet = new Bitmap((tile * 2) + layout, layout);

        using (var g = Graphics.FromImage(sheet))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (var dark = new SolidBrush(Color.FromArgb(0x2a, 0x30, 0x38)))
            {
                g.FillRectangle(dark, 0, 0, tile, layout);
                g.FillRectangle(dark, tile * 2, 0, layout, layout);
            }

            using (var bright = new SolidBrush(Color.FromArgb(0xe4, 0xe8, 0xec)))
                g.FillRectangle(bright, tile, 0, tile, layout);

            var top = (layout - size) / 2;

            foreach (var x in (int[])[margin, tile + margin])
                g.DrawImage(icon, new Rectangle(x, top, size, size));

            // 表示位置の図。1m を何pxで描くか（マイクの幅 0.05m を 96px）。
            var pixelsPerMeter = 96f / ResetWarningOverlay.MicSizeMeters;
            var center = new PointF((tile * 2) + (layout / 2f), layout / 2f);
            var mic = ResetWarningOverlay.MicCenter;

            // マイクの代わりの形（VRChat の絵は同梱しない）。縦長の角丸と台。
            using (var micBrush = new SolidBrush(Color.FromArgb(0xc8, 0xcc, 0xd0)))
            {
                var h = ResetWarningOverlay.MicSizeMeters * pixelsPerMeter;
                var w = h * ((434f - 78f) / 512f);
                using var capsule = new System.Drawing.Drawing2D.GraphicsPath();
                var body = new RectangleF(center.X - (w * 0.28f), center.Y - (h / 2f), w * 0.56f, h * 0.62f);
                capsule.AddArc(body.X, body.Y, body.Width, body.Width, 180, 180);
                capsule.AddArc(body.X, body.Bottom - body.Width, body.Width, body.Width, 0, 180);
                capsule.CloseFigure();
                g.FillPath(micBrush, capsule);
                g.FillRectangle(micBrush, center.X - (w * 0.06f), center.Y + (h * 0.18f), w * 0.12f, h * 0.24f);
                g.FillRectangle(micBrush, center.X - (w * 0.3f), center.Y + (h * 0.42f), w * 0.6f, h * 0.08f);
            }

            foreach (var position in ResetWarningPositions.Order)
            {
                var offset = ResetWarningOverlay.OffsetFor(position, ResetWarningOptions.DefaultScale);
                var side = ResetWarningOverlay.SizeMeters * pixelsPerMeter;
                var cx = center.X + ((offset.X - mic.X) * pixelsPerMeter);
                var cy = center.Y - ((offset.Y - mic.Y) * pixelsPerMeter);

                using var attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = position == ResetWarningPositions.Default ? 1f : 0.25f });
                g.DrawImage(icon, Rectangle.Round(new RectangleF(cx - (side / 2f), cy - (side / 2f), side, side)), 0, 0, size, size, GraphicsUnit.Pixel, attributes);
            }
        }

        var full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        sheet.Save(full, System.Drawing.Imaging.ImageFormat.Png);

        var texture = Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + "-texture.png");
        icon.Save(texture, System.Drawing.Imaging.ImageFormat.Png);

        Console.WriteLine($"保存先       : {full}（{sheet.Width}×{sheet.Height}px・履歴リセットの予告のアイコン）");
        Console.WriteLine($"アイコンの絵 : {texture}（{size}×{size}px）");

        foreach (var position in ResetWarningPositions.Order)
        {
            // 実際に置く位置（奥行き1.15mへ広げたもの→実装メモ5.94）。
            var offset = ResetWarningOverlay.PlacementFor(position, ResetWarningOptions.DefaultScale);
            Console.WriteLine($"{ResetWarningPositions.DisplayName(position),-4}         : X {offset.X:+0.000;-0.000} Y {offset.Y:+0.000;-0.000} Z {offset.Z:+0.000;-0.000} m（幅 {ResetWarningOverlay.WidthFor(ResetWarningOptions.DefaultScale):0.000} m）");
        }

        return ExitCode.Success;
    }

    /// <summary>
    /// 「行を指して目印を選んでいる」状態の見本（→実装メモ5.32）。
    ///
    /// 既に目印の付いている行を指し、その印を選択中・別の印を指している状態にする。
    /// 選択肢の3つの見た目（通常・指している・選択中）が1枚で分かる並びを選んである。
    /// </summary>
    private static PanelDecorations WithMarkPopup(
        PanelDecorations decorations,
        PanelRenderer renderer,
        IReadOnlyList<RowLayout> layouts,
        float scrollOffset)
    {
        // 見えている行のうち、目印が付いていて注意（指す側）とは違い、滞在中でないものを選ぶ
        // （右端のボタンは既定の「ブラウザで開く」。「ここへ戻る」にしても押せる見た目になる行→実装メモ5.53）。
        var index = Math.Max(0, layouts.Count - 3);

        for (var i = layouts.Count - 1; i >= 0; i--)
        {
            var mark = layouts[i].Row.Mark;

            if (mark is InstanceMark.None or InstanceMark.Warning || !layouts[i].Row.Returnable)
                continue;

            var rect = PanelGeometry.RowHighlightRect(
                renderer.Style,
                renderer.ViewportHeight,
                scrollOffset,
                renderer.RowAt(layouts, i).Top,
                renderer.RowAt(layouts, i).Height);

            // 半分以上が見えている行だけを選ぶ（端で切れた行では見本にならない）。
            if (rect.Height < renderer.Style.RowHeight / 2f)
                continue;

            index = i;
            break;
        }

        var hit = renderer.RowAt(layouts, index);

        var row = PanelGeometry.RowHighlightRect(
            renderer.Style,
            renderer.ViewportHeight,
            scrollOffset,
            hit.Top,
            hit.Height);

        var popup = PanelGeometry.MarkPopupRect(renderer.Style, renderer.ViewportHeight, row, InstanceMarks.Choices.Count);

        Console.WriteLine($"目印の見本  : {index + 1}行目を指している状態（ポップアップ {popup.Width}×{popup.Height}px）");

        return decorations with
        {
            HoverRow = row,
            Popup = popup,
            PopupCurrent = layouts[index].Row.Mark,
            PopupPointed = InstanceMark.Warning,
            PopupReturnAction = Core.Locations.ReturnAction.Browser,
            PopupReturnEnabled = layouts[index].Row.CanOpen(Core.Locations.ReturnAction.Browser),
        };
    }
}
