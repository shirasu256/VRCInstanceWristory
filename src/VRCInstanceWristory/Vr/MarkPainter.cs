using System.Drawing;
using System.Drawing.Drawing2D;
using VRCInstanceWristory.Core.Marks;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 目印（ハート・チェック・注意）の描画（2026-09-22のユーザー指定→実装メモ5.32）。
///
/// 指定は絵文字（❤️✅⚠️）で受けたが、絵文字のフォントをそのまま描くと、
/// 色数の多い立体的な絵がパネルの平面的な見た目から浮く。環境によっては字体が変わり、
/// 日本語フォントに字形がないと豆腐（□）になる。
/// そこで<b>図形として描く</b>。どのPCでも同じ形になり、線の太さと色をパネルへ揃えられる。
///
/// 形は 100×100 の座標で組み立て、渡された正方形へ当てはめる。
/// 角の丸みはアンチエイリアスで滑らかにする（背景の矩形と違い、面の縁が
/// 背景の不透明度に影響しないため。滞在時間の棒の半円と同じ扱い→実装メモ5.28）。
/// </summary>
public static class MarkPainter
{
    /// <summary>図形を組み立てる基準の一辺。</summary>
    private const float Unit = 100f;

    /// <summary>
    /// 目印を <paramref name="bounds"/> へ描く。正方形でない矩形を渡した場合は、
    /// 短い辺に合わせて中央へ置く（形をゆがめない）。
    /// </summary>
    public static void Draw(Graphics graphics, InstanceMark mark, RectangleF bounds, Color color)
    {
        if (mark == InstanceMark.None)
            return;

        var size = MathF.Min(bounds.Width, bounds.Height);
        if (size <= 0f)
            return;

        var scale = size / Unit;
        var offsetX = bounds.X + ((bounds.Width - size) / 2f);
        var offsetY = bounds.Y + ((bounds.Height - size) / 2f);

        var smoothing = graphics.SmoothingMode;
        var state = graphics.Save();

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(offsetX, offsetY);
        graphics.ScaleTransform(scale, scale);

        switch (mark)
        {
            case InstanceMark.Heart:
                DrawHeart(graphics, color);
                break;

            case InstanceMark.Check:
                DrawCheck(graphics, color);
                break;

            case InstanceMark.Warning:
                DrawWarning(graphics, color);
                break;
        }

        graphics.Restore(state);
        graphics.SmoothingMode = smoothing;
    }

    /// <summary>塗りつぶしのハート。左右のふくらみをベジェ曲線2組で作る。</summary>
    private static void DrawHeart(Graphics graphics, Color color)
    {
        using var path = new GraphicsPath();

        path.AddBezier(50f, 89f, 22f, 71f, 8f, 54f, 8f, 37f);
        path.AddBezier(8f, 37f, 8f, 23f, 19f, 15f, 30f, 15f);
        path.AddBezier(30f, 15f, 39f, 15f, 46f, 20f, 50f, 27f);
        path.AddBezier(50f, 27f, 54f, 20f, 61f, 15f, 70f, 15f);
        path.AddBezier(70f, 15f, 81f, 15f, 92f, 23f, 92f, 37f);
        path.AddBezier(92f, 37f, 92f, 54f, 78f, 71f, 50f, 89f);
        path.CloseFigure();

        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }

    /// <summary>太い線のチェック。端と折れは丸めて、離れて見ても形が潰れないようにする。</summary>
    private static void DrawCheck(Graphics graphics, Color color)
    {
        using var pen = new Pen(color, 15f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        graphics.DrawLines(pen, [new PointF(13f, 52f), new PointF(39f, 76f), new PointF(87f, 24f)]);
    }

    /// <summary>
    /// 塗りつぶしの三角に「！」を抜いた注意の印。
    /// 「！」を背景色で塗るのではなく<b>穴</b>にしてあるので、行の上でもポップアップの上でも同じに見える。
    /// </summary>
    private static void DrawWarning(Graphics graphics, Color color)
    {
        using var path = new GraphicsPath { FillMode = FillMode.Alternate };

        path.AddPolygon([new PointF(50f, 9f), new PointF(97f, 89f), new PointF(3f, 89f)]);

        // 内側の2つは、Alternate の塗り分けで穴になる。
        path.AddRectangle(new RectangleF(44f, 36f, 12f, 28f));
        path.AddEllipse(43.5f, 69f, 13f, 13f);

        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }
}
