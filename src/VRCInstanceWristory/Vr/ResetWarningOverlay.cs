using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using Valve.VR;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 「あと n 分で履歴がリセットされる」ことを知らせるアイコン（2026-09-28のユーザー指定→実装メモ5.87。設定は5.89）。
///
/// VRChatのマイクのアイコンの横（既定は左下）に並べて、2秒間隔で点滅して消える。頭（HMD）に固定するオーバーレイ1枚で、
/// 絵は作ったときに1回だけ渡し、点滅は <c>SetOverlayAlpha</c> だけで行う（絵を渡し直さないのでちらつかない→5.34）。
///
/// VRChatはHUDのマイクのアイコンの位置を外へ出していない。大きさと奥行きは、VRChatのマイクのアイコンを
/// OpenVRのオーバーレイで置き換える OSS の VRCMicOverlay（rrazgriz/VRCMicOverlay・MIT）の既定値に合わせ、
/// 中心は利用者が実機で合わせた値を焼き込んだ（VRCMicOverlay の既定から 左1.5cm・下5.5cm→実装メモ5.93）。
///
/// | 値 | マイクの想定（<see cref="MicCenter"/>） | このアイコン |
/// | --- | --- | --- |
/// | 左右・上下（m） | -0.385・-0.315 | マイクの図の端から <see cref="GapMeters"/> あけ、設定の向き（8方向）へ並べる（<see cref="OffsetFor"/>）。図の縦幅は0.06m |
/// | 前後（m・負が前） | -0.92 | 同じ |
/// | 幅（m） | 0.05（VRCMicOverlay） | 同じ×設定の倍率（0.6〜1.4。図は円なので、等倍でマイクの縦の長さとほぼ揃う） |
/// </summary>
public sealed class ResetWarningOverlay
{
    private const string Key = "vrcinstancewristory.resetwarning";

    /// <summary>
    /// VRChatのマイクのアイコンの中心の想定。頭の座標系（m）。
    /// 5.87 では VRCMicOverlay の既定（-0.37, -0.26, -0.92）を使ったが、利用者の実機では合わず、2026-09-30 に利用者が
    /// 「マイクアイコン位置」で合わせた値（左 1.5cm・下 5.5cm）を焼き込んだ（→実装メモ5.93）。
    /// OyasumiVR の想定（-0.48, -0.405, -1.15 を奥行き0.92mに直すと -0.384, -0.324）にも近い。
    /// </summary>
    public static readonly Vector3 MicCenter = new(-0.385f, -0.315f, -0.92f);

    /// <summary>VRChatのマイクのアイコンの幅（m）。VRCMicOverlay の既定で、VRChatの既定の大きさにほぼ等しい。</summary>
    public const float MicSizeMeters = 0.05f;

    /// <summary>
    /// マイクの図の幅の割合。VRCMicOverlay の絵（512px角）では、図が横 78〜434px にある。
    /// 縦長の図なので、並べる間隔はオーバーレイの端ではなく図の端から測る。
    /// </summary>
    private const float MicGlyphWidthRatio = (434f - 78f) / 512f;

    /// <summary>
    /// マイクの図の縦幅（m）。VRCMicOverlay の絵の縦いっぱい（0.05m）より 1cm 広い 0.06m とした
    /// （2026-09-30 の利用者の実機での合わせ→実装メモ5.93）。上・下に置くときの間に効く。
    /// </summary>
    public const float MicGlyphHeightMeters = 0.06f;

    /// <summary>等倍のときのこのアイコンの幅（m）。マイクと同じ。</summary>
    public const float SizeMeters = MicSizeMeters;

    /// <summary>このアイコンの図（輪と影の縁取り）の幅の割合。絵の縁まで描くと縁取りが切れるので少し内側に収める。</summary>
    public const float GlyphRatio = 0.86f;

    /// <summary>マイクの図の端と、このアイコンの図の端の間（m）。</summary>
    public const float GapMeters = 0.008f;

    /// <summary>
    /// このアイコンの中心。マイクと同じ奥行きで、マイクから見て <paramref name="position"/> の向きへ並べる。
    /// 斜め（左下など）は、左右と上下のどちらにも図の端から <see cref="GapMeters"/> あけた角に置く。
    /// </summary>
    /// <remarks>
    /// 返すのは奥行き0.92m（<see cref="MicCenter"/> の Z）で組み立てた基準の位置。実際に置くのは、これを <see cref="DepthScale"/> 倍した所
    /// （<see cref="PlacementFor"/>→実装メモ5.94）。
    /// </remarks>
    /// <param name="micOffsetCm">マイクアイコンの位置の合わせ（横・縦 cm・右と上が正→実装メモ5.92）。マイクの中心の想定をこれだけ動かしてから並べる。</param>
    public static Vector3 OffsetFor(ResetWarningPosition position, float scale, Vector2 micOffsetCm = default)
    {
        var (dirX, dirY) = ResetWarningPositions.Direction(position);
        var mic = MicCenterFor(micOffsetCm);
        var half = SizeMeters * scale * GlyphRatio / 2f;
        var dx = (MicSizeMeters * MicGlyphWidthRatio / 2f) + GapMeters + half;
        var dy = (MicGlyphHeightMeters / 2f) + GapMeters + half;
        return new Vector3(mic.X + (dirX * dx), mic.Y + (dirY * dy), mic.Z);
    }

    /// <summary>
    /// 実際に置く奥行き（m・頭の正面方向）。基準の0.92mより奥に置いて VRChat のマイクのアイコンと奥行きをそろえる
    /// （2026-09-30の利用者の指定で OyasumiVR の想定 1.15m にした→実装メモ5.94）。
    /// </summary>
    public const float DepthMeters = 1.15f;

    /// <summary>
    /// 基準（奥行き0.92m）から実際の奥行きへの倍率。位置・幅・間をすべてこの倍率で広げるので、目から見た方向と見かけの大きさは変わらない。
    /// 「マイクアイコン位置」の横・縦の cm も基準の奥行きで測った値として扱い、同じ倍率で広げる。
    /// <paramref name="depthOffsetCm"/> は「マイクアイコン位置（奥行き）」（奥が正→実装メモ5.95）。
    /// </summary>
    public static float DepthScaleFor(float depthOffsetCm = 0f) => (DepthMeters + (depthOffsetCm / 100f)) / -MicCenter.Z;

    /// <summary>既定の奥行き（1.15m）での倍率。</summary>
    public static float DepthScale => DepthScaleFor();

    /// <summary>実際に置く位置（頭の座標系・m）。<see cref="OffsetFor"/> を奥行きの倍率で広げたもの。</summary>
    public static Vector3 PlacementFor(ResetWarningPosition position, float scale, Vector2 micOffsetCm = default, float depthOffsetCm = 0f)
        => OffsetFor(position, scale, micOffsetCm) * DepthScaleFor(depthOffsetCm);

    /// <summary>実際に置くときの幅（m）。見かけの大きさを保つため、奥行きと同じ倍率で広げる。</summary>
    public static float WidthFor(float scale, float depthOffsetCm = 0f) => SizeMeters * scale * DepthScaleFor(depthOffsetCm);

    /// <summary>合わせを入れたマイクの中心の想定（頭の座標系・m）。</summary>
    public static Vector3 MicCenterFor(Vector2 micOffsetCm)
        => new(MicCenter.X + (micOffsetCm.X / 100f), MicCenter.Y + (micOffsetCm.Y / 100f), MicCenter.Z);

    /// <summary>
    /// 設定を変えている間にアイコンを出し続ける時間（→実装メモ5.92）。最後の変更からこの時間で消える。
    /// 点滅させずに点けたままにして、VR の中でマイクのアイコンとの並びを見ながら合わせられるようにする。
    /// </summary>
    public static readonly TimeSpan PreviewDuration = TimeSpan.FromSeconds(5);

    /// <summary>絵の大きさ（px・正方形）。</summary>
    public const int TextureSize = 128;

    private readonly SteamVrSession _session;
    private readonly AppSettings _settings;
    private readonly IDiagnostics _log;
    private readonly ResetWarningBlink _blink = new();
    private VrOverlay? _overlay;
    private (ResetWarningPosition Position, float Scale, Vector2 MicOffset, float Depth)? _placed;
    private TimeSpan? _previewUntil;

    public ResetWarningOverlay(SteamVrSession session, AppSettings settings, IDiagnostics log)
    {
        _session = session;
        _settings = settings;
        _log = log;
    }

    public void Create()
    {
        _overlay = _session.CreateOverlay(Key, "VRC Instance Wristory 履歴リセットの予告");
        _overlay.SetInputMethodNone();
        _overlay.SetWidthInMeters(WidthFor(1f));

        // 手首のパネル（10）・カーソル（16）より手前。VRChatのHUDと同じく、ほかの面に隠れないようにする。
        _overlay.SetSortOrder(100);
        _overlay.SetAlpha(0f);
        _overlay.SetVisible(false);

        using var bitmap = Render(TextureSize);
        var pixels = new byte[TextureSize * TextureSize * 4];
        PanelRenderer.CopyPixels(bitmap, pixels);

        if (!_overlay.SetTexture(pixels, TextureSize, TextureSize))
            _log.Warn("履歴リセットの予告のアイコンの絵を渡せませんでした。");
    }

    /// <summary>点滅を始める。回数は設定の「点滅回数」。</summary>
    public void Start(TimeSpan now)
    {
        _blink.Start(now, _settings.ResetWarningBlinkCount);
        _log.Info($"履歴のリセットまで{_settings.ResetWarningLeadMinutes}分を切ったので、予告のアイコンを{_settings.ResetWarningBlinkCount}回点滅させます。");
    }

    /// <summary>
    /// 設定を変えたので、しばらくアイコンを点けたままにする（→<see cref="PreviewDuration"/>・実装メモ5.92）。点滅の途中なら点滅をやめる。
    /// </summary>
    public void Preview(TimeSpan now)
    {
        _blink.Stop();
        _previewUntil = now + PreviewDuration;
    }

    /// <summary>設定を変えている間の表示をしているか。</summary>
    private bool Previewing(TimeSpan now) => _previewUntil is { } until && now < until;

    /// <summary>
    /// 1フレーム分の更新。<paramref name="enabled"/> が false の間は出さない（VRオーバーレイ機能か、リセット予告アイコンを表示するがオフ）。
    /// 表示位置・大きさ・不透明度は毎フレーム設定から読むので、変えるとその場で反映する。
    /// </summary>
    public void Update(TimeSpan now, bool enabled)
    {
        if (_overlay is null)
            return;

        if (!enabled)
        {
            _blink.Stop();
            _previewUntil = null;
        }

        var previewing = Previewing(now);

        if (!previewing)
            _previewUntil = null;

        if (!_blink.Active(now) && !previewing)
        {
            _blink.Stop();
            Hide();
            return;
        }

        var placement = (_settings.WarningPosition, _settings.ResetWarningScale, new Vector2(_settings.ResetWarningMicOffsetXCm, _settings.ResetWarningMicOffsetYCm), _settings.ResetWarningMicOffsetZCm);

        if (_placed != placement)
        {
            // 頭に固定する。頭が動いても SteamVR が付いてくるので、置くのは場所・大きさ・合わせを変えたときだけでよい。
            _overlay.SetWidthInMeters(WidthFor(placement.ResetWarningScale, placement.Item4));

            if (_overlay.SetTransformDeviceRelative(OpenVR.k_unTrackedDeviceIndex_Hmd, Transform(placement.WarningPosition, placement.ResetWarningScale, placement.Item3, placement.Item4)))
                _placed = placement;
        }

        var level = previewing ? 1f : _blink.Alpha(now);
        _overlay.SetAlpha(level * Math.Clamp(_settings.ResetWarningOpacity, 0f, 1f));
        _overlay.SetVisible(true);
    }

    public void Hide()
    {
        _overlay?.SetAlpha(0f);
        _overlay?.SetVisible(false);
    }

    /// <summary>
    /// 頭の座標系での姿勢。<see cref="OffsetFor"/> に置き、面を目の方へ向ける（VRCMicOverlay と同じく、
    /// 斜め前に置いた面が視線に対して傾いて見えないようにする）。上は頭の上に保つ。
    /// </summary>
    public static HmdMatrix34_t Transform(ResetWarningPosition position, float scale, Vector2 micOffsetCm = default, float depthOffsetCm = 0f)
    {
        var offset = PlacementFor(position, scale, micOffsetCm, depthOffsetCm);

        // オーバーレイは +Z の側から見える。+Z を目（原点）へ向ける。
        var z = Vector3.Normalize(-offset);
        var x = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z));
        var y = Vector3.Cross(z, x);
        return Math3d.FromAxes(x, y, z, offset);
    }

    /// <summary>予告のアイコンの色（黄色）。2026-09-29のユーザー指定で、枠線なしの黄色一色にした（→実装メモ5.90）。</summary>
    public static readonly Color Color = Color.FromArgb(0xff, 0xd8, 0x3a);

    /// <summary>
    /// アイコンの絵。VRChatのマイクのアイコンと同じく平塗りの図で、<see cref="Color"/> の一色だけで描く（縁取りなし）。
    /// 形は時計回りの矢印の輪（↻＝リセット）の中に、アプリのアイコン（<see cref="SettingsDashboardView.RenderThumbnail"/>）と同じ
    /// 3本の線といちばん下の線の ▶（訪問履歴の一覧）。2026-09-29のユーザー指定で「5」から置き換えた（→実装メモ5.89）。
    /// </summary>
    public static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var scale = size / 100f;
        g.ScaleTransform(scale, scale);

        // 100×100 の座標で組み立てる。輪の外径は80で、矢じりまで含めた幅が GlyphRatio（86）。
        var ring = new RectangleF(15f, 15f, 70f, 70f);
        const float ringWidth = 10f;

        using var ringPath = new GraphicsPath();

        // 右上（-70度）から時計回りに300度回る輪。切れ目は左上に置き、終わりの端に右向きの矢じりを付ける。
        ringPath.AddArc(ring, -70f, 300f);

        using var brush = new SolidBrush(Color);

        using (var pen = new Pen(Color, ringWidth) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
            g.DrawPath(pen, ringPath);

        g.FillPolygon(brush, [new PointF(24f, 4f), new PointF(46f, 16f), new PointF(26f, 34f)]);

        // 輪の内側（直径60）に、アプリのアイコンと同じ割合の3本の線と ▶ を置く。
        const float left = 42f;
        const float right = 68f;
        const float thickness = 6f;
        float[] rows = [36f, 47f, 58f];

        foreach (var y in rows)
            g.FillRectangle(brush, left, y, right - left, thickness);

        var cy = rows[^1] + (thickness / 2f);
        g.FillPolygon(brush, [new PointF(31f, cy - 6f), new PointF(40f, cy), new PointF(31f, cy + 6f)]);

        return bitmap;
    }
}
