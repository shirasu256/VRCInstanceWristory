using System.Drawing;
using System.Numerics;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 操作する手から伸ばす見えないレイと、命中した点に出すカーソル。
/// 何を指しているかは、交点をパネル内のpx座標へ直して決める（→実装メモ5.35）。
/// </summary>
public sealed partial class OverlayController
{
    /// <summary>
    /// 右手のレイがパネルのどこを指しているか（パネル内のpx座標）。
    ///
    /// 交点のUV（<c>VROverlayIntersectionResults_t.vUVs</c>）は上下の向きが環境依存なので、
    /// UVではなく交点の座標そのものから求める。ボタン・行・選択肢の判定はすべてこれを使う。
    /// </summary>
    private bool TryPanelPoint(out PointF point)
    {
        point = default;

        if (!Hit || !_hasHitPoint || !_hasWristPose || !_hasPanelTransform)
            return false;

        var panelWorld = Math3d.Multiply(_lastWristPose, _panelTransform);
        var local = Math3d.InverseTransformPoint(panelWorld, _hitPoint);

        point = PanelGeometry.PixelFromPanelLocal(
            _renderer.Style,
            _panelWidthMeters,
            _renderer.Height,
            local.X,
            local.Y);

        return !float.IsNaN(point.X);
    }

    /// <summary>右手のaim姿勢から見えないレイを飛ばし、命中した点にカーソルを出す。</summary>
    private void UpdateCursor(in InputFrame frame, TimeSpan now)
    {
        if (_cursor is null || _panel is null)
            return;

        if (!PanelVisible || !frame.PointerPoseValid)
        {
            Hit = false;
            _hasHitPoint = false;
            _hitHold.Reset();
            _cursor.SetVisible(false);
            return;
        }

        var origin = Math3d.Translation(frame.PointerPose);
        var direction = Math3d.Normalize(Math3d.Forward(frame.PointerPose), -Vector3.UnitZ);

        if (_panel.TryComputeIntersection(_session.Origin, origin, direction, out var results))
        {
            var normal = Math3d.FromHmd(results.vNormal);

            // 交点がUI矩形の表側にあることを確認する（裏面からは命中としない）。
            if (results.fDistance > 0f && Vector3.Dot(normal, direction) < 0f)
            {
                Hit = true;
                _hitHold.Signal(now);

                var point = Math3d.FromHmd(results.vPoint);

                // 指している行を求めるのに使う（UVは上下の向きが環境依存なので、交点そのものから求める）。
                _hitPoint = point;
                _hasHitPoint = true;
                var up = _hasWristPose
                    ? Math3d.Up(Math3d.Multiply(_lastWristPose, Math3d.FromTransform(_translation, _rotation)))
                    : Vector3.UnitY;

                // 幅は設定から毎回渡す（変わっていなければ VrOverlay が投げ直さない）。
                _cursor.SetWidthInMeters(_settings.CursorSizeMeters);
                _cursor.SetTransformAbsolute(_session.Origin, Math3d.LookAlong(point + normal * 0.003f, normal, up));
                _cursor.SetVisible(true);
                return;
            }
        }

        // 一瞬外れただけならカーソルを消さない。手の微動やフレーム落ちでの点滅を防ぐ。
        if (_hitHold.IsHolding(now))
        {
            Hit = true;
            return;
        }

        Hit = false;
        _hasHitPoint = false;
        _cursor.SetVisible(false);
    }

    /// <summary>中心が抜けた小さな輪。</summary>
    private static byte[] CursorTexture(int size)
    {
        var pixels = new byte[size * size * 4];
        var center = (size - 1) / 2f;
        var outer = size * 0.45f;
        var inner = size * 0.24f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = MathF.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                var alpha = distance <= outer && distance >= inner ? 235 : 0;

                if (distance < inner)
                    alpha = 60;

                // BGRA の順で入れる（テクスチャは B8G8R8A8_UNORM）。色は #73dcde。
                var i = (y * size + x) * 4;
                pixels[i + 0] = 0xde;
                pixels[i + 1] = 0xdc;
                pixels[i + 2] = 0x73;
                pixels[i + 3] = (byte)alpha;
            }
        }

        return pixels;
    }
}
