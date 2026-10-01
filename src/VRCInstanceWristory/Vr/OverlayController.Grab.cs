using Valve.VR;

namespace VRCInstanceWristory.Vr;

/// <summary>パネルを操作する手で掴んで置き直す（→実装メモ5.78）。</summary>
public sealed partial class OverlayController
{
    private bool _grabbing;
    private bool _grabReleased;
    private HmdMatrix34_t _grabOffset;

    /// <summary>
    /// 右手でパネルを掴んで置く。
    /// 掴み始めはレイがUIに命中している間だけ、握り続けている間は命中が外れても追従する。
    /// 離した時点の姿勢をパネルを付けた手を基準にした相対値として確定し、呼び出し側が設定へ保存する。
    /// 設定の「手首パネルの移動」を切っている間は掴めない（→実装メモ5.78）。
    /// </summary>
    private void UpdateGrab(in InputFrame frame)
    {
        var canHold = _settings.PanelGrabEnabled && frame.GrabHeld && frame.PointerPoseValid && _hasWristPose;

        if (!canHold)
        {
            ReleaseGrab();
            return;
        }

        if (!_grabbing)
        {
            // 掴み始めはUIに当てているときだけ。誤操作で勝手に動かさない。
            if (!Hit)
                return;

            // 掴んだ瞬間の、右手から見たパネルの位置関係を覚える。
            var panelWorld = Math3d.Multiply(_lastWristPose, Math3d.FromTransform(_translation, _rotation));
            _grabOffset = Math3d.Multiply(Math3d.Invert(frame.PointerPose), panelWorld);
            _grabbing = true;
        }

        var moved = Math3d.Multiply(frame.PointerPose, _grabOffset);
        var relative = Math3d.Multiply(Math3d.Invert(_lastWristPose), moved);

        _translation = Math3d.Translation(relative);
        _rotation = Math3d.ToQuaternion(relative);
        _transformDirty = true;
    }

    private void ReleaseGrab()
    {
        if (!_grabbing)
            return;

        _grabbing = false;
        _grabReleased = true;
    }
}
