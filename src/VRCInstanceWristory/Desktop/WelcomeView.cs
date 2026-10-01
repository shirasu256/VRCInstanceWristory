using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 初回起動の案内の画面（2026-09-30のユーザー指定）。上に説明、中央に「VRChatのメインメニューを開いている間、手首にパネルが出る」ことの
/// 抽象的なイラスト（文字は入れない）、下にVRモードでの出方の説明と「わかった」を置く。
/// デスクトップのウィンドウが、まだ案内を閉じたことがなければ中身の代わりに全面へ出す（<see cref="DesktopView.ShowWelcome"/>）。
/// 絵は <see cref="CanvasSize"/>（ウィンドウの既定の大きさ）で組み、ウィンドウの大きさに合わせて縦横同じ倍率で縮めて中央に置く。
/// 字と描き手は作ったときに用意して、描くたびには作らない（案内を出している間もマウスが「わかった」を出入りするたびに描き直すため）。
/// </summary>
public sealed class WelcomeView : IDisposable
{
    private const string Lead = "訪問したインスタンスID一覧を履歴として確認できます";
    private const string NoteBefore = "VRモード時はVRChatの";
    private const string NoteEmphasis = "メインメニューを開いている間だけ";
    private const string NoteAfter = "手首にオーバーレイメニューが表示されます";
    private const string ButtonLabel = "わかった";

    /// <summary>絵を組む大きさ（論理px）。</summary>
    private static readonly Size CanvasSize = DesktopView.DefaultClientSize;

    // VRChatのメインメニューを抽象化した色。本物の配色（暗い灰色の面・青緑の差し色・灰色の翼）に寄せつつ、
    // 輝度と彩度を一段落として、光らせた手首のパネルのほうが主役に見えるようにする。
    private static readonly Color MenuPanel = Color.FromArgb(0x19, 0x1d, 0x22);
    private static readonly Color MenuBorder = Color.FromArgb(0x28, 0x2e, 0x34);
    private static readonly Color MenuBar = Color.FromArgb(0x1f, 0x24, 0x2a);
    private static readonly Color MenuSidebar = Color.FromArgb(0x1f, 0x25, 0x2b);
    private static readonly Color MenuBlock = Color.FromArgb(0x24, 0x2a, 0x30);
    private static readonly Color MenuTeal = Color.FromArgb(0x3b, 0x86, 0x8d);
    private static readonly Color MenuTile = Color.FromArgb(0x1d, 0x30, 0x35);
    private static readonly Color MenuTileBorder = Color.FromArgb(0x27, 0x44, 0x49);
    private static readonly Color MenuLine = Color.FromArgb(0x5a, 0x63, 0x6b);
    private static readonly Color MenuWing = Color.FromArgb(0x33, 0x38, 0x3d);
    private static readonly Color MenuTab = Color.FromArgb(0x38, 0x3d, 0x42);

    private static readonly Color Hand = Color.FromArgb(0x3a, 0x47, 0x52);

    /// <summary>手の形（腕の座標系の論理px）を描くときの倍率。</summary>
    private const float HandScale = 1.35f;

    /// <summary>手の太さ（腕に垂直な向き）だけに掛ける倍率。</summary>
    private const float HandThickness = 0.8f;

    /// <summary>手に対する手首のパネルの大きさ。</summary>
    private const float PanelScale = 1.5f * 1.15f;

    /// <summary>手とパネルをまとめた全体の大きさ。</summary>
    private const float ArmScale = 0.75f;

    /// <summary>イラストの座標系の大きさ（論理px）。</summary>
    private const float SceneWidth = 900f;

    private const float SceneHeight = 420f;

    /// <summary>イラストを出す倍率（中身の幅に合わせる）。</summary>
    private const float SceneScale = OnboardingLayout.ContentWidth / SceneWidth;

    /// <summary>イラストと下の説明の間（論理px）。</summary>
    private const float NoteGap = 22f;

    /// <summary>下の説明の字の大きさ（論理px）。中身の幅に1行で収まる大きさ。</summary>
    private const float NoteSize = 18f;

    /// <summary>下の説明を縮めるときの下限と刻み（論理px）。</summary>
    private const float MinNoteSize = 10f;

    private const float NoteShrinkStep = 0.5f;

    /// <summary>「わかった」より上の、見出し・イラスト・説明の高さ（論理px）。</summary>
    private const float BodyHeight = (SceneHeight * SceneScale) + NoteGap + NoteSize;

    private readonly PanelStyle _style;
    private readonly UiPainter _painter;
    private readonly UiPainter _buttonPainter;
    private readonly Font _leadFont;
    private readonly Font _noteFont;
    private readonly float _widthBefore;
    private readonly float _widthEmphasis;
    private readonly float _widthAfter;

    public WelcomeView(PanelStyle style)
    {
        _style = style;
        _painter = new UiPainter(style, 1f);
        _buttonPainter = new UiPainter(style, OnboardingLayout.ButtonScale);
        _leadFont = new Font(_painter.Fonts.JapaneseFamilyName, OnboardingLayout.TitleSize, FontStyle.Bold, GraphicsUnit.Pixel);

        // 下の説明の字。中身の幅を超える環境では縮める（絵は倍率を掛けて描くので、論理pxで1回だけ決めればよい）。
        var noteSize = NoteSize;

        while (true)
        {
            _noteFont = new Font(_painter.Fonts.JapaneseFamilyName, noteSize, FontStyle.Regular, GraphicsUnit.Pixel);
            _widthBefore = _painter.MeasureWidth(NoteBefore, _noteFont);
            _widthEmphasis = _painter.MeasureWidth(NoteEmphasis, _noteFont);
            _widthAfter = _painter.MeasureWidth(NoteAfter, _noteFont);

            if (_widthBefore + _widthEmphasis + _widthAfter <= OnboardingLayout.ContentWidth || noteSize <= MinNoteSize)
                break;

            _noteFont.Dispose();
            noteSize -= NoteShrinkStep;
        }
    }

    /// <summary>まとまり（見出し・中身・ボタン）の上端（<see cref="CanvasSize"/> の座標）。</summary>
    private static float BlockTop
        => MathF.Max(OnboardingLayout.VerticalMargin, (CanvasSize.Height - (OnboardingLayout.TitleSize + OnboardingLayout.TitleGap + BodyHeight + OnboardingLayout.ButtonGap + OnboardingLayout.ButtonSize.Height)) / 2f);

    /// <summary>「わかった」の矩形（<see cref="CanvasSize"/> の座標）。</summary>
    private static RectangleF CanvasButtonRect()
        => new(
            (CanvasSize.Width - OnboardingLayout.ButtonSize.Width) / 2f,
            BlockTop + OnboardingLayout.TitleSize + OnboardingLayout.TitleGap + BodyHeight + OnboardingLayout.ButtonGap,
            OnboardingLayout.ButtonSize.Width,
            OnboardingLayout.ButtonSize.Height);

    /// <summary>絵を <paramref name="client"/> に収める倍率と、左上の位置。</summary>
    private static (float Scale, PointF Offset) Fit(Size client)
    {
        var scale = MathF.Min(client.Width / (float)CanvasSize.Width, client.Height / (float)CanvasSize.Height);
        scale = MathF.Max(scale, 0.05f);
        return (scale, new PointF((client.Width - (CanvasSize.Width * scale)) / 2f, (client.Height - (CanvasSize.Height * scale)) / 2f));
    }

    /// <summary>ウィンドウの中身の座標での「わかった」の矩形。</summary>
    public static RectangleF ButtonRect(Size client)
    {
        var (scale, offset) = Fit(client);
        var r = CanvasButtonRect();
        return new RectangleF(offset.X + (r.X * scale), offset.Y + (r.Y * scale), r.Width * scale, r.Height * scale);
    }

    /// <summary>見本と自動検証のために、<paramref name="client"/> の大きさの画像にする。</summary>
    public static Bitmap Render(PanelStyle style, Size client, bool buttonPointed = true)
    {
        var bitmap = new Bitmap(client.Width, client.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var view = new WelcomeView(style))
        using (var g = Graphics.FromImage(bitmap))
            view.Draw(g, client, buttonPointed);

        return bitmap;
    }

    /// <summary><paramref name="client"/> の大きさの面の全体に描く。</summary>
    public void Draw(Graphics g, Size client, bool buttonPointed)
    {
        g.Clear(UiMetrics.WindowBackground);

        var state = g.Save();
        var (scale, offset) = Fit(client);
        g.TranslateTransform(offset.X, offset.Y);
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var canvas = CanvasSize;

        // 上の説明（初期設定の画面の見出しと同じ大きさ・同じ間隔）。
        var top = BlockTop;
        g.DrawString(Lead, _leadFont, _painter.Brush(_style.Text), (canvas.Width / 2f) - (_painter.MeasureWidth(Lead, _leadFont) / 2f), top, _painter.Format);

        // 中央のイラスト。幅は初期設定の2列の幅にそろえる。
        var area = new RectangleF((canvas.Width - OnboardingLayout.ContentWidth) / 2f, top + OnboardingLayout.TitleSize + OnboardingLayout.TitleGap, OnboardingLayout.ContentWidth, SceneHeight * SceneScale);

        g.FillRectangle(_painter.Brush(_style.Surface), area);
        _painter.DrawBorder(g, area, _style.Rule);
        DrawScene(g, _style, area);

        // 下の説明。「メインメニューを開いている間だけ」をアクセント色にする。
        var noteY = area.Bottom + NoteGap;
        var x = (canvas.Width - (_widthBefore + _widthEmphasis + _widthAfter)) / 2f;
        var text = _painter.Brush(_style.Text);

        g.DrawString(NoteBefore, _noteFont, text, x, noteY, _painter.Format);
        g.DrawString(NoteEmphasis, _noteFont, _painter.Brush(_style.Accent), x + _widthBefore, noteY, _painter.Format);
        g.DrawString(NoteAfter, _noteFont, text, x + _widthBefore + _widthEmphasis, noteY, _painter.Format);

        // 閉じるボタン。
        _buttonPainter.DrawButton(g, CanvasButtonRect(), ButtonLabel, buttonPointed);

        g.Restore(state);
    }

    public void Dispose()
    {
        _leadFont.Dispose();
        _noteFont.Dispose();
        _buttonPainter.Dispose();
        _painter.Dispose();
    }

    /// <summary>
    /// 左に、親指を手前にして真横から見た左手と、手の甲の側から垂直に立てた手首のパネル。右奥にVRChatのメインメニュー。
    /// パネルの基本の位置（手の甲から垂直に立ち、親指の側から真横に見ると正面を向く）に合わせた見え方にする。
    /// </summary>
    private static void DrawScene(Graphics g, PanelStyle style, RectangleF area)
    {
        var state = g.Save();
        g.SetClip(area, CombineMode.Intersect);
        g.TranslateTransform(area.X, area.Y);
        g.ScaleTransform(area.Width / SceneWidth, area.Height / SceneHeight);

        // 横幅は 460 の80%。右の翼の外側は、上と同じだけ（40）空ける（→2026-09-30のユーザー指定）。
        DrawMenu(g, new RectangleF(462f, 40f, 368f, 286f));

        // 腕の座標系：手首を原点に、指先の向きを +X、手の甲の側を −Y にとる。左下から右上へ少し上げる。
        var arm = g.Save();
        g.TranslateTransform(230f, 372f);
        g.RotateTransform(-16f);
        g.ScaleTransform(ArmScale, ArmScale);

        DrawSideHand(g);

        // パネルは手首の甲の側（上の縁）から少し離して、腕に垂直に立てる。手首の真上に中心を置き、
        // 手の甲の盛り上がり（指の付け根）にはかからないようにする。
        var wristTop = -25f * HandScale * HandThickness;
        DrawWristPanel(g, style, new PointF(-10f, wristTop - 36f - (54f * PanelScale)), 0f, PanelScale);

        g.Restore(arm);
        g.Restore(state);
    }

    /// <summary>
    /// 親指を手前にして真横から見た左手のシルエット（腕の座標系）。指はそろえて軽く曲げる。
    /// 前腕は肘の側へ向けて背景に溶かす。
    /// </summary>
    private static void DrawSideHand(Graphics g)
    {
        using var hand = new GraphicsPath();

        var state = g.Save();
        g.ScaleTransform(HandScale, HandScale * HandThickness);

        // 上の縁（前腕・手首・手の甲・指の背）→曲げた指先→下の縁（指の腹・手のひら）→前腕の下の縁。
        hand.StartFigure();
        hand.AddLine(-330f, -36f, -250f, -35f);
        hand.AddBezier(new PointF(-250f, -35f), new PointF(-150f, -33f), new PointF(-50f, -27f), new PointF(0f, -25f));
        hand.AddBezier(new PointF(0f, -25f), new PointF(30f, -24f), new PointF(60f, -38f), new PointF(96f, -38f));
        hand.AddBezier(new PointF(96f, -38f), new PointF(128f, -38f), new PointF(158f, -30f), new PointF(176f, -12f));
        hand.AddBezier(new PointF(176f, -12f), new PointF(190f, 0f), new PointF(192f, 14f), new PointF(185f, 20f));
        hand.AddBezier(new PointF(185f, 20f), new PointF(179f, 26f), new PointF(168f, 24f), new PointF(162f, 21f));
        hand.AddBezier(new PointF(162f, 21f), new PointF(155f, 18f), new PointF(144f, 19f), new PointF(130f, 24f));
        hand.AddBezier(new PointF(130f, 24f), new PointF(108f, 30f), new PointF(88f, 44f), new PointF(60f, 44f));
        hand.AddBezier(new PointF(60f, 44f), new PointF(34f, 44f), new PointF(16f, 30f), new PointF(0f, 28f));
        hand.AddBezier(new PointF(0f, 28f), new PointF(-60f, 32f), new PointF(-150f, 38f), new PointF(-250f, 42f));
        hand.AddLine(-250f, 42f, -330f, 44f);
        hand.CloseFigure();

        // 前腕の肘の側（x が −300〜−150）だけを背景へ溶かし、そこから先は手の色で塗る。
        var clip = g.Save();

        using (var handBrush = new SolidBrush(Hand))
        using (var fadeBrush = new LinearGradientBrush(new PointF(-300f, 0f), new PointF(-150f, 0f), Color.FromArgb(0, Hand), Hand))
        {
            g.SetClip(new RectangleF(-150f, -80f, 400f, 160f), CombineMode.Intersect);
            g.FillPath(handBrush, hand);
            g.Restore(clip);

            clip = g.Save();
            g.SetClip(new RectangleF(-400f, -80f, 250.5f, 160f), CombineMode.Intersect);
            g.FillPath(fadeBrush, hand);
            g.Restore(clip);
        }

        g.Restore(state);
    }

    /// <summary>
    /// VRChatのメインメニューを抽象化したもの。細部は描かず、要素の位置関係だけを残す：
    /// 上のバー、左の大きな区画（選んだインスタンス）と操作のタイルの区画、右のインスタンスの一覧（1つを選択中）、
    /// 左右の翼、下のタブの帯。いちばん伝えたい右の一覧だけは、1件ずつの札と選択中の枠まで描く。
    /// </summary>
    private static void DrawMenu(Graphics g, RectangleF main)
    {
        // 左右の翼（本体の奥）。
        using (var wing = new SolidBrush(MenuWing))
        using (var p = Shapes.RoundedRect(RectangleF.FromLTRB(main.X - 30f, main.Y + 52f, main.Right + 30f, main.Bottom - 20f), 10f))
            g.FillPath(wing, p);

        // 下のタブの列（ひとまとまりの帯にする→2026-09-30のユーザー指定）。
        using (var fill = new SolidBrush(MenuTab))
        using (var p = Shapes.RoundedRect(new RectangleF(main.X + 30f, main.Bottom - 4f, main.Width - 60f, 30f), 4f))
            g.FillPath(fill, p);

        // 本体。
        using (var panel = new SolidBrush(MenuPanel))
        using (var border = new Pen(MenuBorder, 1.5f))
        using (var p = Shapes.RoundedRect(main, 10f))
        {
            g.FillPath(panel, p);
            g.DrawPath(border, p);
        }

        var clipState = g.Save();

        using (var inner = Shapes.RoundedRect(RectangleF.Inflate(main, -1.5f, -1.5f), 9f))
            g.SetClip(inner, CombineMode.Intersect);

        using var block = new SolidBrush(MenuBlock);
        using var line = new SolidBrush(MenuLine);
        using var tile = new SolidBrush(MenuTile);
        using var tileBorder = new Pen(MenuTileBorder, 1.2f);

        // 上のバー（利用者・時刻・アイコンの段と、ワールドの題名とタブの段をひとまとめにする）。
        var bar = new RectangleF(main.X, main.Y, main.Width, 60f);

        using (var fill = new SolidBrush(MenuBar))
            g.FillRectangle(fill, bar);

        g.FillRectangle(line, main.X + 22f, main.Y + 26f, 120f, 8f);

        // 右のインスタンスの一覧。
        var sidebar = RectangleF.FromLTRB(main.Right - 118f, bar.Bottom, main.Right, main.Bottom);

        using (var fill = new SolidBrush(MenuSidebar))
            g.FillRectangle(fill, sidebar);

        using (var selectedBorder = new Pen(MenuTeal, 2f))
        {
            for (var i = 0; i < 5; i++)
            {
                var item = new RectangleF(sidebar.X + 10f, sidebar.Y + 14f + (i * 44f), sidebar.Width - 20f, 36f);

                using var p = Shapes.RoundedRect(item, 4f);
                g.FillPath(tile, p);
                g.DrawPath(i == 0 ? selectedBorder : tileBorder, p);

                g.FillRectangle(line, item.X + 8f, item.Y + 9f, 46f, 5f);
                g.FillRectangle(line, item.X + 8f, item.Y + 20f, 28f, 4f);
            }
        }

        // 左の大きな区画（選んだインスタンス）と、操作のタイルの列。
        var left = RectangleF.FromLTRB(main.X + 18f, bar.Bottom + 18f, sidebar.X - 18f, bar.Bottom + 118f);

        using (var p = Shapes.RoundedRect(left, 5f))
            g.FillPath(block, p);

        // 操作のタイルの列（ひとまとまりの区画にする→2026-09-30のユーザー指定）。
        using (var p = Shapes.RoundedRect(new RectangleF(left.X, left.Bottom + 16f, left.Width, 60f), 4f))
        {
            g.FillPath(tile, p);
            g.DrawPath(tileBorder, p);
        }

        g.Restore(clipState);
    }

    /// <summary>手首のパネル。本物のパネルと同じ配色で、行の中身は棒で表す。</summary>
    private static void DrawWristPanel(Graphics g, PanelStyle style, PointF center, float angle, float scale)
    {
        var state = g.Save();
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(angle);
        g.ScaleTransform(scale, scale);

        var panel = new RectangleF(-72f, -54f, 144f, 108f);

        // 浮いて見えるよう、うっすら光らせる。
        for (var i = 3; i >= 1; i--)
        {
            using var glow = new SolidBrush(Color.FromArgb(22, style.Accent));
            using var p = Shapes.RoundedRect(RectangleF.Inflate(panel, i * 5f, i * 5f), 6f + (i * 5f));
            g.FillPath(glow, p);
        }

        using (var surface = new SolidBrush(style.Surface))
            g.FillRectangle(surface, panel);

        using (var header = new SolidBrush(style.Header))
            g.FillRectangle(header, panel.X, panel.Y, panel.Width, 18f);

        using (var muted = new SolidBrush(style.Muted))
        using (var text = new SolidBrush(style.Text))
        using (var accent = new SolidBrush(style.Accent))
        using (var current = new SolidBrush(style.CurrentRow))
        using (var rule = new SolidBrush(style.Rule))
        {
            g.FillRectangle(muted, panel.X + 7f, panel.Y + 7f, 40f, 5f);
            g.FillRectangle(muted, panel.Right - 32f, panel.Y + 7f, 25f, 5f);

            const float rowHeight = 22.5f;
            var top = panel.Y + 18f;

            for (var i = 0; i < 4; i++)
            {
                var y = top + (i * rowHeight);
                var last = i == 3;

                if (last)
                    g.FillRectangle(current, panel.X, y, panel.Width, rowHeight);
                else
                    g.FillRectangle(rule, panel.X, y + rowHeight - 1f, panel.Width, 1f);

                // 大きな番号と、右の2行の付加情報。
                g.FillRectangle(last ? accent : text, panel.X + 8f, y + 6f, 40f, 10f);
                g.FillRectangle(last ? accent : text, panel.X + 58f, y + 5f, 56f, 4f);
                g.FillRectangle(muted, panel.X + 58f, y + 13f, 74f - (i % 2 * 16f), 4f);
            }

            // 枠はペンではなく塗りで描く（→実装メモ5.18）。
            UiPainter.FillBorder(g, accent, panel, 2f);
        }

        g.Restore(state);
    }
}
