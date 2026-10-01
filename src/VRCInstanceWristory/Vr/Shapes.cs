using System.Drawing;
using System.Drawing.Drawing2D;

namespace VRCInstanceWristory.Vr;

/// <summary>パネル・ダッシュボード・デスクトップのウィンドウで共通に使う図形。</summary>
public static class Shapes
{
    /// <summary>
    /// 角を丸めた矩形の輪郭。角の直径は矩形の短い辺を超えない（小さな矩形でも輪郭が崩れない）。
    /// 使い終わったら破棄する。
    /// </summary>
    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}
