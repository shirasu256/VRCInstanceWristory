using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using Valve.VR;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// SteamVRのダッシュボード（コントローラーのシステムボタンで出る画面）に、設定の画面を出す
/// （2026-09-26のユーザー指定→実装メモ5.40）。OVR Advanced Settings などと同じ「ダッシュボードのオーバーレイ」で、
/// 下のバーにアイコンが並び、選ぶと中身が出る。レーザーでの操作はマウスのイベントとして届く。
///
/// 中身の絵と操作は <see cref="SettingsDashboardView"/>（デスクトップのウィンドウと同じ設定の部品）で、
/// ここはOpenVRとのつなぎだけを持つ。主ループ（単一スレッド）から毎フレーム <see cref="Update"/> を呼ぶ。
/// 操作は <see cref="DesktopCommand"/> として溜め、主ループがウィンドウからの操作と同じ経路で反映する。
/// </summary>
public sealed class SettingsDashboard : IDisposable
{
    private const string Key = "vrcinstancewristory.settings";
    private const string Name = AppInfo.DisplayName;

    /// <summary>
    /// ダッシュボードでの幅。OVR Advanced Settings と同じ 2.5m にした
    /// （ダッシュボードの枠に合わせて出るので、見え方はこの値にほとんど左右されない。
    /// 枠の中での大きさは、絵の周りの透明な余白で決める→<see cref="SettingsDashboardView.AreaRatio"/>・実装メモ5.107）。
    /// </summary>
    private const float WidthMeters = 2.5f;

    private const int ThumbnailSize = 128;

    private readonly SteamVrSession _session;
    private readonly IDiagnostics _log;
    private readonly SettingsDashboardView _view;
    private readonly Queue<DesktopCommand> _commands = new();

    private VrOverlay? _main;
    private VrOverlay? _thumbnail;
    private VREvent_t _event;

    public SettingsDashboard(SteamVrSession session, PanelStyle style, DesktopSettings settings, IDiagnostics log)
    {
        _session = session;
        _log = log;
        _view = new SettingsDashboardView(style, settings, _commands.Enqueue);
    }

    /// <summary>設定の画面の絵を SteamVR へ渡せていないか（→実装メモ5.85）。</summary>
    public bool Failing => _main is null || !_main.TextureReady;

    /// <summary>作って、最初の絵とアイコンを渡す。作れなければ false（手首のパネルはそのまま使える）。</summary>
    public bool Create()
    {
        try
        {
            (_main, _thumbnail) = _session.CreateDashboardOverlay(Key, Name);
        }
        catch (InvalidOperationException ex)
        {
            _log.Warn($"{ex.Message}（設定はデスクトップのウィンドウから変えられます）");
            return false;
        }

        _main.SetWidthInMeters(WidthMeters);
        _main.SetMouseInput(_view.Width, _view.Height);

        // ダッシュボードで選ばれる前から中身を入れておく（選んだ瞬間に空の枠が見えないように）。
        Submit();

        using (var icon = SettingsDashboardView.RenderThumbnail(new PanelStyle(), ThumbnailSize))
        {
            var pixels = PanelRenderer.CopyPixels(icon, new byte[ThumbnailSize * ThumbnailSize * 4]);
            _thumbnail.SetTexture(pixels, ThumbnailSize, ThumbnailSize);
        }

        _log.Info($"SteamVRのダッシュボードに設定の画面を登録しました（{_view.Width}×{_view.Height}px）。");
        return true;
    }

    /// <summary>もう一方の画面（デスクトップのウィンドウ）で変わった設定を受け取る。</summary>
    public void SetSettings(DesktopSettings settings) => _view.SetSettings(settings);

    /// <summary>SteamVR・Windows の登録の状態だけを差し替える（→実装メモ5.50・5.51）。</summary>
    public void SetStartupState(bool? launchWithSteamVr, bool launchAtLogon) => _view.SetStartupState(launchWithSteamVr, launchAtLogon);

    /// <summary>ダッシュボードでの操作を1つ取り出す。</summary>
    public bool TryTakeCommand(out DesktopCommand command)
        => _commands.TryDequeue(out command!);

    /// <summary>毎フレーム呼ぶ。レーザーの操作を受け、見た目が変わっていれば描き直して渡す。</summary>
    public void Update(TimeSpan now)
    {
        if (_main is null)
            return;

        while (_main.PollEvent(ref _event))
            HandleEvent(now);

        _view.Update(now);

        // 見えていない間は描き直さない（開いたときに描く）。値は写しに入っているので失われない。
        if (_view.Dirty && _main.ShownBySteamVr)
            Submit();
    }

    private void HandleEvent(TimeSpan now)
    {
        var mouse = _event.data.mouse;

        switch ((EVREventType)_event.eventType)
        {
            case EVREventType.VREvent_MouseMove:
                _view.PointerMove(SettingsDashboardView.FromOverlayMouse(mouse.x, mouse.y, _view.Height));
                break;

            case EVREventType.VREvent_MouseButtonDown when mouse.button == (uint)EVRMouseButton.Left:
                _view.PointerDown(SettingsDashboardView.FromOverlayMouse(mouse.x, mouse.y, _view.Height), now);
                break;

            case EVREventType.VREvent_MouseButtonUp when mouse.button == (uint)EVRMouseButton.Left:
                _view.PointerUp();
                break;

            case EVREventType.VREvent_FocusLeave:
            case EVREventType.VREvent_OverlayHidden:
                // レーザーが外れた・ダッシュボードを閉じた。指し示しと押し続けを解く。
                _view.PointerUp();
                _view.PointerMove(null);
                break;
        }
    }

    private void Submit()
    {
        _view.Render();

        if (!_main!.SetTexture(_view.GetPixels(), _view.Width, _view.Height))
            _log.Error("ダッシュボードの設定の画面を渡せませんでした。");
    }

    public void Dispose() => _view.Dispose();
}
