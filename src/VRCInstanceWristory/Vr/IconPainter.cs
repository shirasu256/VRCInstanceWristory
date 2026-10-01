using System.Drawing;
using System.Drawing.Drawing2D;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 目印以外の小さな印（写真・「ここへ戻る」・「ブラウザで開く」）の描画（2026-09-26のユーザー指定→実装メモ5.43・5.47・5.53）。
///
/// 目印（<see cref="MarkPainter"/>）と同じく、絵文字のフォントは使わず図形として描く。
/// どのPCでも同じ形になり、線の太さと色をパネルへ揃えられる。形は 100×100 の座標で組み立て、
/// 渡された正方形へ当てはめる。
/// </summary>
public static class IconPainter
{
    private const float Unit = 100f;

    /// <summary>写真の印（カメラ）。行の1段目の右端に、枚数と並べて出す。</summary>
    public static void DrawCamera(Graphics graphics, RectangleF bounds, Color color)
        => Draw(graphics, bounds, g =>
        {
            // 本体とその上の出っ張りを1つの形にし、レンズを穴にして真ん中に点を残す（Alternate の塗り分け）。
            using var path = new GraphicsPath { FillMode = FillMode.Alternate };

            using (var body = Shapes.RoundedRect(new RectangleF(6f, 26f, 88f, 62f), 12f))
                path.AddPath(body, connect: false);

            path.AddEllipse(28f, 34f, 44f, 44f);
            path.AddEllipse(40f, 46f, 20f, 20f);

            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);

            // 上の出っ張り（ファインダー）。本体とは別に塗る（重なりで穴にならないよう少し離す）。
            using var top = Shapes.RoundedRect(new RectangleF(30f, 12f, 40f, 16f), 5f);
            g.FillPath(brush, top);
        });

    /// <summary>
    /// 「ここへ戻る」の印。上から回り込んで左を指す矢印で、元の場所へ引き返すことを表す。
    /// </summary>
    public static void DrawReturn(Graphics graphics, RectangleF bounds, Color color)
        => Draw(graphics, bounds, g =>
        {
            using var pen = new Pen(color, 13f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Flat,
                LineJoin = LineJoin.Round,
            };

            // 下の左端から右へ進み、右側の半円で上へ回って、左へ戻る。
            using var path = new GraphicsPath();
            path.AddLine(22f, 80f, 58f, 80f);
            path.AddArc(30f, 24f, 56f, 56f, 90f, -180f);
            path.AddLine(58f, 24f, 40f, 24f);
            g.DrawPath(pen, path);

            // 矢じりは左向きの三角。
            using var brush = new SolidBrush(color);
            g.FillPolygon(brush, [new PointF(12f, 24f), new PointF(42f, 6f), new PointF(42f, 42f)]);
        });

    /// <summary>
    /// 「ブラウザで開く」の印（地球・→実装メモ5.53）。円に経線と緯線を引いた、Webページを表す一般的な形。
    /// </summary>
    public static void DrawGlobe(Graphics graphics, RectangleF bounds, Color color)
        => Draw(graphics, bounds, g =>
        {
            using var pen = new Pen(color, 9f) { LineJoin = LineJoin.Round };

            // 外の円・真ん中の経線（縦長の楕円）・赤道と、その上下の緯線。
            g.DrawEllipse(pen, 8f, 8f, 84f, 84f);
            g.DrawEllipse(pen, 30f, 8f, 40f, 84f);
            g.DrawLine(pen, 8f, 50f, 92f, 50f);
            g.DrawLine(pen, 17f, 28f, 83f, 28f);
            g.DrawLine(pen, 17f, 72f, 83f, 72f);
        });

    private static void Draw(Graphics graphics, RectangleF bounds, Action<Graphics> draw)
    {
        var size = MathF.Min(bounds.Width, bounds.Height);
        if (size <= 0f)
            return;

        var scale = size / Unit;
        var offsetX = bounds.X + ((bounds.Width - size) / 2f);
        var offsetY = bounds.Y + ((bounds.Height - size) / 2f);

        var state = graphics.Save();

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(offsetX, offsetY);
        graphics.ScaleTransform(scale, scale);

        draw(graphics);

        graphics.Restore(state);
    }
}
