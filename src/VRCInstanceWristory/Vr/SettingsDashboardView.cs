using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// SteamVRのダッシュボードに出す設定の画面（2026-09-26のユーザー指定→実装メモ5.40）。
///
/// 中身はデスクトップのウィンドウの右側と同じ <see cref="SettingsView"/> で、ダッシュボードの横長の枠に
/// 収まるよう2列に並べ、上に表題の帯を付ける。ウィンドウだけの「このウィンドウ」以外のまとまりをすべて出す。OpenVRには触れない（絵を作って、ポインターを受けるだけ）ので、
/// SteamVRなしで検証でき、<c>--render-sample --dashboard</c> で見た目を確かめられる。
/// OpenVRとのつなぎは <see cref="SettingsDashboard"/>。
///
/// 設定の画面の周りには透明な余白を足し、ダッシュボードの枠の中で <see cref="AreaRatio"/> の面積に見せる（→実装メモ5.107）。
///
/// 座標はテクスチャの画素（余白を含む）で、左上が原点。ダッシュボードのマウス座標は左下が原点なので、
/// 受け取った側が <see cref="FromOverlayMouse"/> で直してから渡す。
/// </summary>
public sealed class SettingsDashboardView : IDisposable
{
    /// <summary>
    /// 絵の大きさを決める倍率。ダッシュボードは視野の中で大きく出るので、
    /// ウィンドウ（等倍）の2倍の画素で並べたときの大きさを絵の大きさ（＝ダッシュボードの枠に収まる範囲）にする。
    /// </summary>
    public const float FrameScale = 2f;

    /// <summary>
    /// 部品（字・ボタン・余白）を描く倍率。ダッシュボードの枠に対して部品が大きすぎたので、
    /// 絵の幅（<see cref="FrameScale"/>）はそのままに、部品だけを70%にした（2026-09-27のユーザー指定→実装メモ5.76）。
    /// 空いた幅は2つの列を広げて埋め、高さは部品の下端で切り詰める（→5.77）。
    /// </summary>
    public const float Scale = FrameScale * 0.7f;

    private const float Margin = 16f;
    private const float HeaderHeight = 48f;

    /// <summary>
    /// ダッシュボードの枠の中で設定の画面が占める面積の割合（2026-10-01のユーザー指定で75%→実装メモ5.107）。
    /// ダッシュボードは絵を枠に合わせて引き伸ばして出すので（→5.40・5.76）、幅（メートル）や絵の画素を変えても見た目は小さくならない。
    /// そこで設定の画面の画素はそのままに、周りへ透明な余白を足した大きな絵を渡す。縦横とも √0.75 ≒ 0.866 倍に見える。
    /// </summary>
    public const float AreaRatio = 0.75f;

    /// <summary>−／＋ を押し続けたときの繰り返し（最初の間と、その後の間隔）。ウィンドウと同じ。</summary>
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(70);

    private static readonly Color Background = Color.FromArgb(0x0c, 0x11, 0x15);

    // 表題と説明は 2026-10-01 のユーザー指定で短くした（→実装メモ5.106）。アプリ名は右端の版に出ている。
    private const string Title = "設定";

    private const string Subtitle = "一部の設定はデスクトップウィンドウ上でのみ変更できます。";

    private readonly PanelStyle _style;
    private readonly SettingsView _settings;
    private readonly UiPainter _painter;
    private readonly Bitmap _bitmap;
    private readonly Graphics _graphics;
    private readonly byte[] _pixels;

    private TimeSpan _nextRepeat;

    public SettingsDashboardView(PanelStyle style, DesktopSettings settings, Action<DesktopCommand> emit)
    {
        _style = style;
        _painter = new UiPainter(style, Scale);
        // 左の列が右より短く下が空くので、「リセットまでの時間」とインスタンスタイプの選択肢を
        // 列の幅で取れる最大の大きさにする（2026-10-01のユーザー指定→実装メモ5.106）。
        _settings = new SettingsView(style, settings, emit) { EmphasizeToFit = true };

        // 設定の画面の幅は、70%にする前（FrameScale）の並びで決める。ダッシュボードは絵を枠の幅に合わせて出すので、
        // 幅を変えずに部品だけを小さく描けば、見た目の部品が70%になる。
        var frameMargin = Margin * FrameScale;
        var frame = _settings.Layout(new PointF(frameMargin, (HeaderHeight * FrameScale) + frameMargin), FrameScale, columns: 2, SettingsSections.Dashboard);
        var contentWidth = (int)MathF.Ceiling(frame.Width + (frameMargin * 2f));

        // 高さは70%で並べた部品の下端までにして、下に空白を残さない（2026-09-27のユーザー指定→実装メモ5.77）。
        var margin = _painter.S(Margin);
        var columnWidth = (contentWidth - (margin * 2f) - _painter.S(SettingsView.ColumnGap)) / 2f;
        var used = _settings.Layout(new PointF(margin, _painter.S(HeaderHeight) + margin), Scale, columns: 2, SettingsSections.Dashboard, columnWidth);
        var contentHeight = (int)MathF.Ceiling(_painter.S(HeaderHeight) + used.Height + (margin * 2f));

        // 周りに透明な余白を足して、ダッシュボードの枠の中で AreaRatio の面積に見せる（→実装メモ5.107）。
        // 縦横の比は変えないので、枠がどちらの向きで合わせても同じ割合になる。設定の画面は真ん中に置く。
        var linear = MathF.Sqrt(AreaRatio);
        Width = (int)MathF.Ceiling(contentWidth / linear);
        Height = (int)MathF.Ceiling(contentHeight / linear);
        Content = new Rectangle((Width - contentWidth) / 2, (Height - contentHeight) / 2, contentWidth, contentHeight);

        // 部品は設定の画面の位置へ並べ直す。当たり判定もテクスチャの画素のままで済む。
        _settings.Layout(new PointF(Content.X + margin, Content.Y + _painter.S(HeaderHeight) + margin), Scale, columns: 2, SettingsSections.Dashboard, columnWidth);

        _bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        _graphics = Graphics.FromImage(_bitmap);
        _pixels = new byte[Width * Height * 4];
    }

    /// <summary>テクスチャの幅（画素）。ダッシュボードのマウス座標もこの大きさで受ける。</summary>
    public int Width { get; }

    public int Height { get; }

    /// <summary>テクスチャの中で設定の画面を描く範囲（周りは透明な余白→実装メモ5.107）。</summary>
    public Rectangle Content { get; }

    public Bitmap Bitmap => _bitmap;

    public DesktopSettings Settings => _settings.Settings;

    /// <summary>「リセットまでの時間」とインスタンスタイプの選択肢に掛けている倍率（ほかの部品に対して→実装メモ5.106）。</summary>
    public float EmphasisScale => _settings.EmphasisScale;

    /// <summary>描き直しが必要か。</summary>
    public bool Dirty => _settings.Dirty;

    /// <summary>
    /// ダッシュボードのマウス座標（OpenVRの決まりで左下が原点。openvr.h の <c>VREvent_Mouse_t</c>）を、
    /// 左上が原点のテクスチャの画素へ直す。
    /// </summary>
    public static PointF FromOverlayMouse(float x, float y, int height) => new(x, height - y);

    /// <summary>デスクトップのウィンドウで変わった設定を受け取る。送り返しはしない。</summary>
    public void SetSettings(DesktopSettings settings) => _settings.SetSettings(settings);

    /// <summary>SteamVR・Windows の登録の状態だけを差し替える（→実装メモ5.50・5.51）。</summary>
    public void SetStartupState(bool? launchWithSteamVr, bool launchAtLogon) => _settings.SetStartupState(launchWithSteamVr, launchAtLogon);

    public void PointerMove(PointF? point) => _settings.PointerMove(point);

    public void PointerDown(PointF point, TimeSpan now)
    {
        if (_settings.PointerDown(point) && _settings.Repeating)
            _nextRepeat = now + RepeatDelay;
    }

    public void PointerUp() => _settings.PointerUp();

    /// <summary>毎フレーム呼ぶ。−／＋ を押し続けている間だけ、決まった間隔で値を動かす。</summary>
    public void Update(TimeSpan now)
    {
        if (!_settings.Repeating || now < _nextRepeat)
            return;

        _settings.RepeatPress();
        _nextRepeat = now + RepeatInterval;
    }

    /// <summary>絵を作り直す。</summary>
    public void Render()
    {
        var g = _graphics;
        g.Clear(Color.Transparent);

        using (var background = new SolidBrush(Background))
            g.FillRectangle(background, Content);

        // ClearTypeは色の付いた縁を作り、VRの中では目立つので使わない（パネルと同じ→3節）。
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.None;

        var header = _painter.S(HeaderHeight);
        var margin = _painter.S(Margin);
        var fonts = _painter.Fonts;
        var left = Content.X;
        var top = Content.Y;

        using (var brush = new SolidBrush(_style.Header))
            g.FillRectangle(brush, left, top, Content.Width, header);

        using (var rule = new SolidBrush(_style.Rule))
            g.FillRectangle(rule, left, top + header, Content.Width, MathF.Max(1f, MathF.Round(_painter.S(1f))));

        using (var text = new SolidBrush(_style.Text))
            g.DrawString(Title, fonts.Title, text, left + margin, top + _painter.S(7f), _painter.Format);

        using (var muted = new SolidBrush(_style.Muted))
        {
            g.DrawString(Subtitle, fonts.Absence, muted, left + margin, top + _painter.S(27f), _painter.Format);

            var version = AppInfo.NameWithVersion;
            var width = _painter.MeasureWidth(version, fonts.Absence);
            g.DrawString(version, fonts.Absence, muted, Content.Right - margin - width, top + _painter.S(27f), _painter.Format);
        }

        _settings.Render(g);
    }

    /// <summary>いまの絵の画素（BGRA）。</summary>
    public byte[] GetPixels() => PanelRenderer.CopyPixels(_bitmap, _pixels);

    /// <summary>設定の部品の矩形（検証用）。</summary>
    public RectangleF? TargetRect(SettingsView.HitKind kind, int index = 0) => _settings.TargetRect(kind, index);

    public string StepperText(int index) => _settings.StepperText(index);

    /// <summary>
    /// ダッシュボードの下のバーに出すアイコン。訪問履歴の一覧（行の線と、いまいる行の ▶）を図にした。
    /// </summary>
    public static Bitmap RenderThumbnail(PanelStyle style, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var unit = size / 16f;
        var box = new RectangleF(unit, unit, size - (unit * 2f), size - (unit * 2f));

        using (var fill = new SolidBrush(style.Surface))
        using (var path = Shapes.RoundedRect(box, unit * 2.5f))
        {
            g.FillPath(fill, path);

            using var edge = new Pen(style.Accent, unit * 0.6f);
            g.DrawPath(edge, path);
        }

        // 3行ぶんの線。いちばん下（いまいる行）だけアクセント色にして ▶ を付ける。
        var left = box.X + (unit * 4.2f);
        var right = box.Right - (unit * 2.2f);
        var thickness = unit * 1.3f;

        for (var i = 0; i < 3; i++)
        {
            var y = box.Y + (unit * (3.4f + (i * 3.2f)));
            var current = i == 2;

            using var brush = new SolidBrush(current ? style.Accent : style.Muted);
            g.FillRectangle(brush, left, y, right - left, thickness);

            if (!current)
                continue;

            var cx = box.X + (unit * 2.2f);
            var cy = y + (thickness / 2f);
            g.FillPolygon(brush,
            [
                new PointF(cx - (unit * 0.9f), cy - (unit * 1.2f)),
                new PointF(cx + (unit * 1.1f), cy),
                new PointF(cx - (unit * 0.9f), cy + (unit * 1.2f)),
            ]);
        }

        return bitmap;
    }

    public void Dispose()
    {
        _settings.Dispose();
        _painter.Dispose();
        _graphics.Dispose();
        _bitmap.Dispose();
    }
}
