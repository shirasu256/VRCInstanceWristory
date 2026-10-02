using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Scrolling;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// デスクトップのウィンドウの中身（2026-09-26のユーザー指定→実装メモ5.39）。
///
/// 左に手首のパネルと同じ絵（同じ <see cref="PanelRenderer"/> で描いて縮小したもの）、右にタブで切り替える
/// 「選んだ行」と設定、左下に動作の状態（<see cref="StatusBar"/>）を出す（右側をタブにしたのは→実装メモ5.42）。
/// パネルはマウスで操作でき、ホイールでスクロール、行のクリックでその行を選んで「選んだ行」に詳しい情報を出し、
/// 右クリックでVR内と同じ目印のポップアップ（目印と「ブラウザで開く」／「ここへ戻る」）を開き、見出しの「延長」のクリックで延長、
/// 「リセット」のクリックでVR内と同じ確認を出す（→実装メモ5.65）。
/// 初回起動の案内と初期設定の画面（<see cref="OnboardingView"/>）を出している間は、中身の代わりにそれを全面へ出す。
///
/// ウィンドウ（Win32）とは切り離してあり、画面の大きさと倍率を与えれば <see cref="Render"/> で1枚の絵になる。
/// そのため、自動検証と <c>--render-sample --window</c> はウィンドウを開かずに同じ絵を確かめられる
/// （そのために中を覗くものは <c>DesktopView.Inspection.cs</c> にまとめてある）。
/// 設定の部品もパネルと同じくGDI+で描き、パネルの配色（<see cref="PanelStyle"/>）に合わせる。
///
/// スクロール位置・指している行・選んだ行・ポップアップはVR内のパネルとは別に持つ（マウスとレイは同時に別の所を指すため）。
/// 行の内容・残り時間・目印は同じものを見る。
/// </summary>
public sealed partial class DesktopView : IDisposable
{
    // 寸法は論理px（96dpi）。実際の画素は表示倍率を掛ける。

    private const float Margin = 16f;
    private const float TabHeight = 30f;
    private const float TabGap = 10f;

    /// <summary>タブとタブの間。</summary>
    private const float TabSpacing = 4f;

    /// <summary>パネルの下端と状態の段の間に取る余白。</summary>
    private const float PanelBottomGap = 8f;

    /// <summary>1ノッチ（120）でスクロールする量。標準行の半分。</summary>
    private const float WheelRows = 0.5f;

    /// <summary>設定を1ノッチで送る量（論理px）。</summary>
    private const float SettingsWheelStep = 60f;

    /// <summary>ウィンドウのパネルに入れられる行の上限（面の大きさを決める）。表示領域はウィンドウの高さまで。</summary>
    private const float DesktopVisibleRows = 24f;

    /// <summary>既定のウィンドウの中身の大きさ（論理px）。</summary>
    public static readonly Size DefaultClientSize = new(1060, 760);

    /// <summary>
    /// これより小さくはしない（論理px）。右の設定が縦に収まる高さ。
    /// 1920×1080・表示倍率125%でも作業領域に収まる。
    /// </summary>
    public static readonly Size MinClientSize = new(860, 660);

    /// <summary>タブの名前と、そのタブに並べる設定のまとまり。</summary>
    private static readonly (DesktopTab Tab, string Label, SettingsSections Sections)[] Tabs =
    [
        (DesktopTab.Details, "インスタンス詳細", SettingsSections.None),
        // 「インスタンス操作」「グループ名」は 2026-09-27 に「一般設定」へ移した（→実装メモ5.71）。
        // タブの名前だけは日英の間を空けない（2026-09-27のユーザー指定→実装メモ5.72）。
        (DesktopTab.Panel, "VRオーバーレイ設定", SettingsSections.Overlay | SettingsSections.WristPanel | SettingsSections.ResetWarning),
        (DesktopTab.Startup, "一般設定", SettingsSections.History | SettingsSections.TargetTypes | SettingsSections.Startup | SettingsSections.Rows | SettingsSections.Photos | SettingsSections.Window | SettingsSections.External | SettingsSections.Update),
    ];

    // 部品。配色はこのウィンドウの写し（パネルの面の大きさ・背景の不透明度を、渡した側に響かせずに変える）。
    private readonly PanelStyle _style;
    private readonly PanelRenderer _panel;
    private readonly ScrollController _scroll;
    private readonly Action<DesktopCommand> _emit;
    private readonly SettingsView _settingsView;
    private readonly RowDetailsView _rowDetails;
    private readonly StatusBar _statusBar;
    private readonly OnboardingView _onboarding;

    // 状態の段の「新しい版 … に更新」を押した（→実装メモ5.121）。ウィンドウが TakeUpdateRequest で取り出す。
    private bool _pendingUpdateLink;

    // 描き手（表示倍率ごと）と大きさ。
    private UiPainter _painter;
    private float _scale = 1f;
    private Size _client;
    private bool _dirty = true;

    // パネルの中身（主ループから届いたもの）。
    private List<RowLayout> _layouts = [];
    private Dictionary<string, RowDetail> _details = new(StringComparer.Ordinal);
    private string _countdown = Countdown.Format(Countdown.Max);
    private bool _countdownStopped;
    private bool _rowsDirty = true;

    // 配置（EnsureLayout で決める）。
    private bool _layoutDirty = true;
    private float _panelScale;
    private RectangleF _statusRect;
    private readonly RectangleF[] _tabRects = new RectangleF[Tabs.Length];

    // 右側の設定の見えている範囲と、その中のスクロール位置（→実装メモ5.67）。
    // 最小の大きさ（860×660・150%）では「VRオーバーレイ設定」が縦に入り切らないので、ホイールで送る。
    private RectangleF _settingsViewport;
    private float _settingsScroll;
    private float _settingsScrollMax;

    // インスタンス詳細の範囲（出していなければ空→実装メモ5.71）。
    private RectangleF _detailsRect;

    // 右側のタブ。起動したときは設定のタブ（それまでのウィンドウと同じく設定が見える）。
    private DesktopTab _tab = DesktopTab.Startup;

    // マウス。
    private PointF? _mouse;
    private int _tabPointed = -1;

    // パネルの操作（VR側の OverlayController と同じ規則をマウスで行う）。
    private PanelRenderer.RowHit _hoverRow;
    private bool _hasHoverRow;
    private bool _resetPointed;
    private bool _clearPointed;
    private bool _confirmOpen;
    private int _confirmPointed = -1;
    private string? _popupEventId;
    private RectangleF _popupRect;
    private int _pointedChoice = -1;

    // 選んだ行（→実装メモ5.42）。「選んだ行」のタブに詳しい情報を出す。
    private string? _selectedEventId;

    public DesktopView(PanelStyle style, DesktopSettings settings, Action<DesktopCommand> emit)
    {
        _style = style.Copy();
        _emit = emit;

        // ウィンドウのパネルは、行が少なくても枠を下まで描く（→実装メモ5.71）。表示領域の高さはウィンドウの高さで決め、
        // そのために手首のパネル（7.5行）より多くの行を入れられる面を用意する。
        _style.VisibleRows = DesktopVisibleRows;
        _panel = new PanelRenderer(_style);
        _scroll = new ScrollController
        {
            RowHeight = _style.RowHeight,
            ViewportHeight = _panel.ViewportHeight,
        };

        _painter = new UiPainter(_style, _scale);
        _settingsView = new SettingsView(_style, settings, OnSettingsCommand) { FileDialogs = true };
        _rowDetails = new RowDetailsView(_style, emit) { ReturnAction = settings.ReturnAction };
        _statusBar = new StatusBar(_style);
        _statusBar.Layout(RectangleF.Empty, _painter);
        _onboarding = new OnboardingView(_style, _settingsView, emit);
    }

    public Size ClientSize => _client;

    public DesktopSettings Settings => _settingsView.Settings;

    /// <summary>インスタンス操作の挙動（→実装メモ5.53）。ポップアップと「選んだ行」のボタンの文字が変わる。</summary>
    public ReturnAction ReturnAction => _settingsView.Settings.ReturnAction;

    /// <summary>写真のサムネイルを返す（写真の場所 → 絵→実装メモ5.55）。ウィンドウが置き場所から読む。</summary>
    public Func<string, Image?>? Thumbnails
    {
        get => _rowDetails.Thumbnails;
        set => _rowDetails.Thumbnails = value;
    }

    /// <summary>サムネイルができた（描き直す）。</summary>
    public void ThumbnailsChanged() => _rowDetails.ThumbnailsChanged();

    /// <summary>
    /// 描き直しが必要か。ウィンドウはこれが立ったときだけ <see cref="Render"/> を呼ぶ。
    /// 右側は見えているタブの部品だけを見る。見えていない側は描かないので印が下りず、立ったままだと
    /// 何があっても全体を描き直すことになり、状態の段の点だけを描き直す点滅（→実装メモ5.91）も働かない。
    /// 初回起動の案内・初期設定の画面を出している間は、そちらの印だけを見る。タブを切り替えたときは全体を描き直す。
    /// </summary>
    public bool Dirty
    {
        get => _dirty || (_onboarding.IsOpen ? _onboarding.Dirty : _tab == DesktopTab.Details ? _rowDetails.Dirty : _settingsView.Dirty);
        private set => _dirty = value;
    }

    /// <summary>ボタンを押しっぱなしにしている（ウィンドウが押し続けの繰り返しを進める）。</summary>
    public bool Repeating => (_onboarding.SetupOpen || _tab != DesktopTab.Details) && _settingsView.Repeating;

    /// <summary>状態の段に点滅する点（赤・黄）があるか。あればウィンドウが点だけを描き直し続ける（→実装メモ5.91）。</summary>
    public bool StatusBlinking => !_onboarding.IsOpen && _statusBar.Blinking;

    /// <summary>
    /// 最小化したらタスクトレイへ入れるか。初回起動の案内と初期設定の画面を出している間は入れず、タスクバーに残す
    /// （トレイに入ると見失い、初期設定を終えられないため。2026-10-01のユーザー指定→実装メモ5.113）。
    /// </summary>
    public bool MinimizesToTray => !_onboarding.IsOpen;

    private float S(float logical) => logical * _scale;

    // ------------------------------------------------------------------ 初回起動の案内と初期設定

    /// <summary>初回起動の案内を出す。「わかった」で初期設定の画面へ、「はじめる」でふだんの画面へ進む（→<see cref="OnboardingView"/>）。</summary>
    public void ShowWelcome()
    {
        _onboarding.ShowWelcome();
        _layoutDirty = true;
        Dirty = true;
    }

    // ------------------------------------------------------------------ 主ループから届くもの

    /// <summary>行の内容を差し替える。VR側と同じく、内容が同じなら描き直さない。</summary>
    public void SetRows(IReadOnlyList<DisplayRow> rows)
    {
        if (rows.Count == _layouts.Count && rows.Select((r, i) => r == _layouts[i].Row).All(same => same))
            return;

        // 表示領域の高さは行数ではなくウィンドウの高さで決める（EnsureLayout）。行は上に寄せて並ぶ。
        _layouts = _panel.Measure(rows);
        _scroll.ViewportHeight = _panel.ViewportHeight;
        _scroll.SetRows(_panel.ToScrollRows(_layouts));

        // 開いていたポップアップの行が消えた（期限切れ・読み直し）ときは閉じる。
        if (_popupEventId is not null && IndexOfRow(_popupEventId) < 0)
            ClosePopup();

        // 選んでいた行が消えたら、選んでいないことにする。
        if (_selectedEventId is not null && IndexOfRow(_selectedEventId) < 0)
            _selectedEventId = null;

        _rowsDirty = true;
        RefreshDetails();
        RefreshPointing();
        Dirty = true;
    }

    /// <summary>行ごとの詳しい情報を差し替える（→実装メモ5.42）。</summary>
    public void SetDetails(IReadOnlyList<RowDetail> details)
    {
        _details = details.ToDictionary(d => d.EventId, StringComparer.Ordinal);
        RefreshDetails();
    }

    /// <summary>カウントダウンが止まっているか（→実装メモ5.73）。</summary>
    public void SetCountdownStopped(bool stopped)
    {
        if (stopped == _countdownStopped)
            return;

        _countdownStopped = stopped;
        RefreshPointing();
        Dirty = true;
    }

    public void SetCountdown(string countdown)
    {
        if (countdown == _countdown)
            return;

        _countdown = countdown;
        Dirty = true;
    }

    public void SetStatus(DesktopStatus status)
    {
        if (!_statusBar.SetStatus(status))
            return;

        // 1列目の幅は値の長さで決まるので、置き場所を決め直す（並べる前なら、並べるときに決まる）。
        if (!_layoutDirty)
        {
            _statusBar.RelayoutItems();
            RefreshPointing();
        }

        Dirty = true;
    }

    /// <summary>ダッシュボードで変わった設定を受け取る（→実装メモ5.40）。送り返しはしない。</summary>
    public void SetSettings(DesktopSettings settings)
    {
        _settingsView.SetSettings(settings);
        ApplyOpacity(settings.BackgroundOpacity);
        ApplyReturnAction(settings.ReturnAction);
    }

    /// <summary>SteamVR・Windows の登録の状態だけを差し替える（→実装メモ5.50・5.51）。</summary>
    public void SetStartupState(bool? launchWithSteamVr, bool launchAtLogon)
        => _settingsView.SetStartupState(launchWithSteamVr, launchAtLogon);

    /// <summary>設定の部品での操作。このウィンドウの絵に出るもの（背景の不透明度など）はここで反映してから、主ループへ渡す。</summary>
    private void OnSettingsCommand(DesktopCommand command)
    {
        if (command is DesktopCommand.ChangeSettings change)
        {
            if (change.Fields.HasFlag(SettingsField.BackgroundOpacity))
                ApplyOpacity(change.Settings.BackgroundOpacity);

            if (change.Fields.HasFlag(SettingsField.ReturnAction))
                ApplyReturnAction(change.Settings.ReturnAction);
        }

        _emit(command);
    }

    /// <summary>
    /// いまの見出しの状態（残り時間・履歴の自動リセット→実装メモ5.71・カウントダウンの停止→実装メモ5.73）。
    /// 見出しのボタンの位置はこれで決まるので、描くときも命中を見るときもこれを渡す。
    /// </summary>
    private PanelDecorations HeaderState() => new()
    {
        Countdown = _countdown,
        AutoResetDisabled = !Settings.AutoResetEnabled,
        CountdownStopped = _countdownStopped,
    };

    /// <summary>インスタンス操作の挙動が変わった。ポップアップと「選んだ行」のボタンの文字を変える。</summary>
    private void ApplyReturnAction(ReturnAction action)
    {
        if (_rowDetails.ReturnAction == action)
            return;

        _rowDetails.ReturnAction = action;
        RefreshPointing();
        Dirty = true;
    }

    private void ApplyOpacity(float opacity)
    {
        if (_style.BackgroundOpacity == opacity)
            return;

        _style.BackgroundOpacity = opacity;
        _rowsDirty = true;
        Dirty = true;
    }

    /// <summary>選んだ行の詳しい情報を「選んだ行」のページへ渡し直す。</summary>
    private void RefreshDetails()
    {
        var index = IndexOfRow(_selectedEventId);

        if (index < 0)
        {
            _rowDetails.SetRow(null, null);
            return;
        }

        _rowDetails.SetRow(_layouts[index].Row, _details.GetValueOrDefault(_layouts[index].Row.EventId));
    }

    // ------------------------------------------------------------------ 大きさ

    /// <summary>ウィンドウの中身の大きさ（画素）と表示倍率を合わせる。</summary>
    public void Resize(Size client, float scale)
    {
        if (client == _client && scale == _scale)
            return;

        if (scale != _scale)
        {
            var old = _painter;
            _painter = new UiPainter(_style, scale);
            _scale = scale;

            // 並べ直す（EnsureLayout）までの間に行や状態が届いても、手放した字で測らないよう、先に新しい描き手を渡し直す。
            // 矩形は古いままだが、並べ直すときに決め直す。
            _rowDetails.Layout(_detailsRect, _painter);
            _statusBar.Layout(_statusRect, _painter);
            old.Dispose();
        }

        _client = client;
        _layoutDirty = true;
        Dirty = true;
    }

    // ------------------------------------------------------------------ 配置

    private void EnsureLayout()
    {
        if (!_layoutDirty)
            return;

        _layoutDirty = false;

        var width = _client.Width;
        var height = _client.Height;
        var margin = S(Margin);

        // 右側（1列）。上にタブ、その下にタブの中身。
        // 5.71で幅が足りるとき「インスタンス詳細」を独立させたが、大きさを変える間の描画が重いので 5.73 でやめた。
        // 左のパネルが等倍（それ以上は伸ばさない）に達したら、余った幅は右側を広げて埋める（2026-09-27のユーザー指定→実装メモ5.74）。
        var statusHeight = StatusBar.Height(_scale);
        var availableHeight = MathF.Max(0f, height - margin - statusHeight - margin - S(PanelBottomGap));
        var smallest = _style.HeaderHeight + _style.FooterHeight + _style.RowHeight;
        var widestPanel = _style.Width * MathF.Min(_scale, availableHeight / smallest);
        var columnWidth = MathF.Max(S(SettingsView.ColumnWidth), width - (margin * 3f) - widestPanel);
        var sidebarX = width - margin - columnWidth;
        var contentTop = margin + S(TabHeight) + S(TabGap);
        var tabWidth = (columnWidth - (S(TabSpacing) * (Tabs.Length - 1))) / Tabs.Length;

        for (var i = 0; i < Tabs.Length; i++)
            _tabRects[i] = new RectangleF(sidebarX + (i * (tabWidth + S(TabSpacing))), margin, tabWidth, S(TabHeight));

        LayoutSettings(sidebarX, contentTop, columnWidth, height - margin);

        _detailsRect = _tab == DesktopTab.Details ? new RectangleF(sidebarX, contentTop, columnWidth, MathF.Max(S(120f), height - margin - contentTop)) : RectangleF.Empty;
        _rowDetails.Layout(_detailsRect, _painter);

        // 左下の状態。
        var leftWidth = MathF.Max(0f, sidebarX - (margin * 2f));
        _statusRect = new RectangleF(margin, height - margin - statusHeight, leftWidth, statusHeight);
        _statusBar.Layout(_statusRect, _painter);

        // 左のパネル。縮尺は幅で決め、表示領域はウィンドウの下（状態の段の上）まで伸ばす（→実装メモ5.71）。
        // 行が少なくても枠は下まで描き、行は上に寄せる。画素を引き延ばしてぼかさないよう、等倍より大きくはしない。
        // 高さが足りないときは、行が1行は入るところまで縮める。
        _panelScale = MathF.Min(_scale, MathF.Min(leftWidth / _style.Width, availableHeight / smallest));
        _panelScale = MathF.Max(_panelScale, 0.05f);

        var viewport = (int)MathF.Floor(availableHeight / _panelScale) - _style.HeaderHeight - _style.FooterHeight;

        if (_panel.SetViewportHeightTo(viewport))
        {
            _scroll.ViewportHeight = _panel.ViewportHeight;
            _scroll.SetRows(_panel.ToScrollRows(_layouts));
            _rowsDirty = true;
        }

        // 初期設定の画面を出している間は、設定の部品をその画面に並べる（右側のタブの並びは、閉じたときに並べ直す）。
        _onboarding.Layout(_client, _scale);

        RefreshPointing();
    }

    /// <summary>
    /// 右側の設定を並べる。見えている範囲より長ければ送れるようにする。
    /// 長さは並べてみないと分からないので、並べてから送り位置を丸めて並べ直す。
    /// </summary>
    private void LayoutSettings(float x, float top, float columnWidth, float bottom)
    {
        var sections = _tab == DesktopTab.Details ? SettingsSections.None : Tabs[(int)_tab].Sections;

        _settingsViewport = new RectangleF(x, top, columnWidth, MathF.Max(0f, bottom - top));
        _settingsView.Layout(new PointF(x, top - _settingsScroll), _scale, columns: 1, sections, columnWidth);
        _settingsScrollMax = sections == SettingsSections.None ? 0f : MathF.Max(0f, _settingsView.Bounds.Height - _settingsViewport.Height);

        var clamped = Math.Clamp(_settingsScroll, 0f, _settingsScrollMax);

        if (clamped != _settingsScroll)
        {
            _settingsScroll = clamped;
            _settingsView.Layout(new PointF(x, top - _settingsScroll), _scale, columns: 1, sections, columnWidth);
        }
    }

    /// <summary>右側のタブを切り替える。</summary>
    public void SelectTab(DesktopTab tab)
    {
        if (tab == _tab)
            return;

        _tab = tab;
        _settingsScroll = 0f;
        _layoutDirty = true;
        Dirty = true;
    }

    /// <summary>その位置が設定の見えている範囲の中か。範囲の外（送って隠れた部品）は押せない。</summary>
    private bool InSettings(PointF point) => _tab != DesktopTab.Details && _settingsViewport.Contains(point);

    /// <summary>その位置がインスタンス詳細の中か。</summary>
    private bool InDetails(PointF point) => !_detailsRect.IsEmpty && _detailsRect.Contains(point);

    // ------------------------------------------------------------------ マウス

    /// <summary>マウスが動いた。見た目が変わったら <see cref="Dirty"/> が立つ。</summary>
    public void MouseMove(PointF point)
    {
        _mouse = point;
        RefreshPointing();
    }

    public void MouseLeave()
    {
        _mouse = null;
        RefreshPointing();
    }

    /// <summary>左ボタンを押した。</summary>
    public void MouseDown(PointF point)
    {
        _mouse = point;
        EnsureLayout();

        // 初回起動の案内は「わかった」だけ、初期設定の画面は設定の部品と「はじめる」だけを受け付ける。
        if (_onboarding.IsOpen)
        {
            if (_onboarding.Press(point, _client) != OnboardingView.PressResult.None)
            {
                _layoutDirty = true;
                Dirty = true;
                EnsureLayout();
            }

            RefreshPointing();
            return;
        }

        // 履歴リセットの確認を出している間は、どこを押しても先に確認が受け取る（→実装メモ5.65）。
        if (_confirmOpen)
        {
            PressWhileConfirm();
            RefreshPointing();
            return;
        }

        // パネルの中の操作。ポップアップを開いている間は、どこを押しても先にポップアップが受け取る。
        if (_popupEventId is not null)
        {
            PressWhilePopup();
            RefreshPointing();
            return;
        }

        for (var i = 0; i < Tabs.Length; i++)
        {
            if (!_tabRects[i].Contains(point))
                continue;

            SelectTab(Tabs[i].Tab);
            RefreshPointing();
            return;
        }

        if (_statusBar.CreditLinkRect.Contains(point))
        {
            _emit(new DesktopCommand.OpenDeveloperPage());
            return;
        }

        // 状態の段の「新しい版 … に更新」（→実装メモ5.121）。確かめる画面はウィンドウ（Win32）が出す。
        if (_statusBar.UpdateLinkClickable && _statusBar.UpdateLinkRect.Contains(point))
        {
            _pendingUpdateLink = true;
            return;
        }

        if (TryPanelPoint(point, out var panelPoint))
        {
            if (_panel.ResetButtonRectFor(HeaderState()).Contains(panelPoint))
            {
                _emit(new DesktopCommand.ExtendRetention());
                Dirty = true;
                return;
            }

            // 「リセット」はすぐには消さず、VR内と同じ確認を出す（→実装メモ5.65）。
            if (_panel.ClearButtonRectFor(HeaderState()).Contains(panelPoint))
            {
                OpenConfirm();
                return;
            }

            // 行をクリックしたら、その行を選んで「選んだ行」に詳しい情報を出す（→実装メモ5.42）。
            if (TryFindRow(panelPoint, out var hit))
                SelectRow(_layouts[hit.Index].Row.EventId);

            return;
        }

        if (InSettings(point))
        {
            _settingsView.PointerDown(point);
            return;
        }

        if (InDetails(point))
            _rowDetails.PointerDown(point);
    }

    /// <summary>
    /// 左ボタンのダブルクリック。1回目の押下は <see cref="MouseDown"/> が受け取っており、2回目の押下のあとに呼ぶ。
    /// 「インスタンス詳細」の写真のサムネイルは、ダブルクリックで開く（→実装メモ5.65）。
    /// </summary>
    public void MouseDoubleClick(PointF point)
    {
        _mouse = point;
        EnsureLayout();

        if (InDetails(point) && !_onboarding.IsOpen && !_confirmOpen && _popupEventId is null)
            _rowDetails.DoubleClick(point);
    }

    /// <summary>
    /// 右ボタンを押した。行の上なら、VR内でトリガーを引いたときと同じ目印のポップアップ（目印と「ブラウザで開く」／「ここへ戻る」）を開く。
    /// ポップアップを開いている間なら、何も変えずに閉じる。
    /// </summary>
    public void RightMouseDown(PointF point)
    {
        _mouse = point;
        EnsureLayout();

        if (_onboarding.IsOpen)
            return;

        if (_confirmOpen)
        {
            CloseConfirm();
            RefreshPointing();
            return;
        }

        if (_popupEventId is not null)
        {
            ClosePopup();
            RefreshPointing();
            return;
        }

        // 選んだ行とタブはそのままにする（VR内と同じく、ポップアップだけで付け外しと開くことができる）。
        if (TryPanelPoint(point, out var panelPoint) && TryFindRow(panelPoint, out var hit))
            OpenPopup(hit);
    }

    /// <summary>左ボタンを押し続けている間の繰り返し（−／＋）。ウィンドウがタイマーで呼ぶ。</summary>
    public void RepeatPress() => _settingsView.RepeatPress();

    public void MouseUp() => _settingsView.PointerUp();

    /// <summary>ホイール。<paramref name="delta"/> は1ノッチで120（上が正）。</summary>
    public void MouseWheel(PointF point, int delta)
    {
        _mouse = point;
        EnsureLayout();

        if (_onboarding.IsOpen)
        {
            _onboarding.Wheel(point, delta);
            return;
        }

        if (CurrentPanelRect().Contains(point))
        {
            // ポップアップや確認を出している間はスクロールしない（VR側と同じ）。
            if (_popupEventId is not null || _confirmOpen)
                return;

            _scroll.ScrollBy(-delta / 120f * WheelRows * _style.RowHeight);
            RefreshPointing();
            Dirty = true;
            return;
        }

        if (!InSettings(point))
            return;

        // 入り切らない設定は、どこで回しても送る（→実装メモ5.67・5.71）。送っている途中で −／＋ の行が
        // ポインターの下へ来ても値を動かさないため。送る必要がないときだけ、−／＋ の行の上で値を動かす。
        if (_settingsScrollMax <= 0f)
        {
            _settingsView.Wheel(point, delta);
            return;
        }

        var next = Math.Clamp(_settingsScroll - (delta / 120f * S(SettingsWheelStep)), 0f, _settingsScrollMax);

        if (next == _settingsScroll)
            return;

        _settingsScroll = next;
        _layoutDirty = true;
        EnsureLayout();
        Dirty = true;
    }

    /// <summary>その位置が押せる部品の上か（手の形のカーソルにする）。</summary>
    public bool IsClickable(PointF point)
    {
        EnsureLayout();

        if (_onboarding.IsOpen)
            return _onboarding.IsClickable(point, _client);

        if (TryPanelPoint(point, out var panelPoint))
        {
            if (_confirmOpen)
                return PanelGeometry.ConfirmItemAt(ConfirmBox, panelPoint) >= 0;

            return _popupEventId is not null
                ? PointedChoiceAt(panelPoint) >= 0
                : _panel.ResetButtonRectFor(HeaderState()).Contains(panelPoint)
                  || _panel.ClearButtonRectFor(HeaderState()).Contains(panelPoint)
                  || TryFindRow(panelPoint, out _);
        }

        if (_tabRects.Any(r => r.Contains(point)))
            return true;

        if (_statusBar.CreditLinkRect.Contains(point))
            return true;

        if (_statusBar.UpdateLinkClickable && _statusBar.UpdateLinkRect.Contains(point))
            return true;

        if (InSettings(point))
            return _settingsView.IsClickable(point);

        return InDetails(point) && _rowDetails.IsClickable(point);
    }

    /// <summary>グループ名を打ち込む欄を開いてほしい、という依頼（→実装メモ5.48）。ウィンドウが取り出して欄を置く。</summary>
    public TextEditRequest? TakeTextEditRequest() => _rowDetails.TakeTextEditRequest();

    /// <summary>打ち込んだグループ名を確定する。空なら名前を外す。</summary>
    public void CommitGroupName(string groupId, string text) => _rowDetails.CommitGroupName(groupId, text);

    /// <summary>写真を開くアプリを選んでほしい、という依頼（→実装メモ5.55）。ウィンドウがファイルを選ぶ画面を出す。</summary>
    public bool TakeAppChoiceRequest() => _settingsView.TakeAppChoiceRequest();

    /// <summary>写真を開くアプリを選んだ。</summary>
    public void ChoosePhotoViewer(string executablePath) => _settingsView.ChoosePhotoViewer(executablePath);

    /// <summary>グループ名の読み込み・書き出しのファイルを選んでほしい、という依頼（→実装メモ5.65）。ウィンドウがファイルを選ぶ画面を出す。</summary>
    public GroupFileRequest TakeGroupFileRequest() => _settingsView.TakeGroupFileRequest();

    /// <summary>
    /// 「更新して再起動」（設定のボタンか、状態の段のリンク）を押した、という依頼を取り出す（→実装メモ5.121）。
    /// </summary>
    public bool TakeUpdateRequest()
    {
        var fromLink = _pendingUpdateLink;
        _pendingUpdateLink = false;
        return _settingsView.TakeUpdateRequest() | fromLink;
    }

    /// <summary>いま知らせているアップデートの状態（確かめる画面の文に使う）。</summary>
    public UpdateStatus UpdateStatus => _settingsView.Settings.Update ?? UpdateStatus.Unavailable;

    // ------------------------------------------------------------------ パネルの操作

    /// <summary>画面の位置を、パネル内のpx座標へ直す。パネルの外なら false。</summary>
    private bool TryPanelPoint(PointF point, out PointF panelPoint)
    {
        EnsureLayout();
        panelPoint = default;

        var rect = CurrentPanelRect();
        if (!rect.Contains(point))
            return false;

        panelPoint = new PointF((point.X - rect.X) / _panelScale, (point.Y - rect.Y) / _panelScale);
        return true;
    }

    /// <summary>いまの行数での、画面上のパネルの矩形。</summary>
    private RectangleF CurrentPanelRect()
        => new(S(Margin), S(Margin), _style.Width * _panelScale, _panel.Height * _panelScale);

    /// <summary>パネル内のpx座標の矩形を、画面上の矩形へ直す。</summary>
    private RectangleF ToScreen(RectangleF panelRect)
    {
        EnsureLayout();
        var panel = CurrentPanelRect();
        return new RectangleF(panel.X + (panelRect.X * _panelScale), panel.Y + (panelRect.Y * _panelScale), panelRect.Width * _panelScale, panelRect.Height * _panelScale);
    }

    /// <summary>行の帯の矩形（パネル内のpx座標・いまのスクロール位置で）。</summary>
    private RectangleF RowRect(PanelRenderer.RowHit hit)
        => PanelGeometry.RowHighlightRect(_style, _panel.ViewportHeight, _scroll.Offset, hit.Top, hit.Height, Scrollable);

    private bool TryFindRow(PointF panelPoint, out PanelRenderer.RowHit hit)
    {
        hit = default;

        if (_layouts.Count == 0)
            return false;

        if (panelPoint.X < 0f || panelPoint.X > PanelGeometry.RowHighlightRight(_style, Scrollable))
            return false;

        var top = _style.ViewportTop;

        if (panelPoint.Y < top || panelPoint.Y >= top + _panel.ViewportHeight)
            return false;

        return _panel.TryHitRow(_layouts, _scroll.Offset + (panelPoint.Y - top), out hit);
    }

    /// <summary>行を選んで、「選んだ行」のタブへ切り替える。</summary>
    private void SelectRow(string eventId)
    {
        // 選んでいる行をもう一度押すと、選んでいない状態（「インスタンス番号をクリックして詳細を表示」）へ戻す（→実装メモ5.75）。
        _selectedEventId = string.Equals(_selectedEventId, eventId, StringComparison.Ordinal) ? null : eventId;
        RefreshDetails();
        SelectTab(DesktopTab.Details);
        Dirty = true;
    }

    /// <summary>マウスの位置から、指している行・ボタン・選択肢を決め直す。</summary>
    private void RefreshPointing()
    {
        if (_onboarding.IsOpen)
        {
            _onboarding.PointerMove(_mouse, _client);
            return;
        }

        var before = (_hasHoverRow, _hoverRow, _resetPointed, _clearPointed, _confirmPointed, _pointedChoice, _popupRect, _tabPointed);

        _settingsView.PointerMove(_mouse is { } sm && InSettings(sm) ? sm : null);
        _rowDetails.PointerMove(_mouse is { } dm && InDetails(dm) ? dm : null);
        _tabPointed = _mouse is { } m0 ? Array.FindIndex(_tabRects, r => r.Contains(m0)) : -1;
        var statusChanged = _statusBar.PointerMove(_mouse);

        _resetPointed = false;
        _clearPointed = false;
        _confirmPointed = -1;
        _pointedChoice = -1;

        if (_confirmOpen)
        {
            _hasHoverRow = false;

            if (_mouse is { } m && TryPanelPoint(m, out var p))
                _confirmPointed = PanelGeometry.ConfirmItemAt(ConfirmBox, p);
        }
        else if (_popupEventId is not null)
        {
            _hoverRow = _panel.RowAt(_layouts, IndexOfRow(_popupEventId));
            _hasHoverRow = true;
            _popupRect = PopupRectFor(_hoverRow);

            if (_mouse is { } m && TryPanelPoint(m, out var p))
                _pointedChoice = PointedChoiceAt(p);
        }
        else
        {
            _hasHoverRow = false;

            if (_mouse is { } m && TryPanelPoint(m, out var p))
            {
                _resetPointed = _panel.ResetButtonRectFor(HeaderState()).Contains(p);
                _clearPointed = _panel.ClearButtonRectFor(HeaderState()).Contains(p);

                if (!_resetPointed && !_clearPointed && TryFindRow(p, out var hit))
                {
                    _hoverRow = hit;
                    _hasHoverRow = true;
                }
            }
        }

        if (statusChanged || before != (_hasHoverRow, _hoverRow, _resetPointed, _clearPointed, _confirmPointed, _pointedChoice, _popupRect, _tabPointed))
            Dirty = true;
    }

    /// <summary>履歴リセットの確認の枠（パネル内のpx座標）。</summary>
    private RectangleF ConfirmBox => PanelGeometry.ConfirmBoxRect(_style, _panel.Height);

    private void OpenConfirm()
    {
        ClosePopup();
        _confirmOpen = true;
        _confirmPointed = -1;
        RefreshPointing();
        Dirty = true;
    }

    private void CloseConfirm()
    {
        _confirmOpen = false;
        _confirmPointed = -1;
        Dirty = true;
    }

    /// <summary>確認を出している間に押した。「リセット」なら消す依頼を送り、ほかは何もせずに閉じる（VR側と同じ）。</summary>
    private void PressWhileConfirm()
    {
        if (_mouse is { } m && TryPanelPoint(m, out var p) && PanelGeometry.ConfirmItemAt(ConfirmBox, p) == PanelGeometry.ConfirmAccept)
            _emit(new DesktopCommand.ClearHistory());

        CloseConfirm();
    }

    private void OpenPopup(PanelRenderer.RowHit hit)
    {
        _popupEventId = _layouts[hit.Index].Row.EventId;
        _pointedChoice = -1;
        RefreshPointing();
        Dirty = true;
    }

    private void ClosePopup()
    {
        _popupEventId = null;
        _popupRect = RectangleF.Empty;
        _pointedChoice = -1;
        Dirty = true;
    }

    /// <summary>ポップアップを開いている間に押した。選択肢なら選び、外なら何も変えずに閉じる（VR側と同じ）。</summary>
    private void PressWhilePopup()
    {
        var eventId = _popupEventId!;

        if (_mouse is { } m && TryPanelPoint(m, out var p))
        {
            var choice = PointedChoiceAt(p);

            if (choice == PanelGeometry.ReturnChoiceIndex)
                _emit(new DesktopCommand.OpenInstance(eventId));
            else if (choice >= 0)
                _emit(new DesktopCommand.SetMark(eventId, InstanceMarks.Choices[choice]));
        }

        ClosePopup();
    }

    /// <summary>ポップアップの中で指している項目。押せない「ここへ戻る」（滞在中の行など）は -1。</summary>
    private int PointedChoiceAt(PointF panelPoint)
    {
        var item = PanelGeometry.PopupItemAt(_popupRect, panelPoint, InstanceMarks.Choices.Count);
        return item == PanelGeometry.ReturnChoiceIndex && !PopupReturnEnabled ? -1 : item;
    }

    private bool PopupReturnEnabled
        => _popupEventId is not null && IndexOfRow(_popupEventId) is var index and >= 0 && _layouts[index].Row.CanOpen(ReturnAction);

    private RectangleF PopupRectFor(PanelRenderer.RowHit hit)
        => PanelGeometry.MarkPopupRect(_style, _panel.ViewportHeight, RowRect(hit), InstanceMarks.Choices.Count);

    /// <summary>行が表示領域に収まらず、スクロールの溝を出しているか（行の帯の右端が変わる→実装メモ5.67）。</summary>
    private bool Scrollable => _scroll.ContentHeight > _panel.ViewportHeight;

    private int IndexOfRow(string? eventId)
    {
        if (eventId is null)
            return -1;

        for (var i = 0; i < _layouts.Count; i++)
        {
            if (string.Equals(_layouts[i].Row.EventId, eventId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private PanelDecorations BuildDecorations()
    {
        var current = _popupEventId is not null && IndexOfRow(_popupEventId) is var index and >= 0
            ? _layouts[index].Row.Mark
            : InstanceMark.None;

        var selected = IndexOfRow(_selectedEventId) is var selectedIndex and >= 0
            ? RowRect(_panel.RowAt(_layouts, selectedIndex))
            : RectangleF.Empty;

        return new PanelDecorations
        {
            Countdown = _countdown,
            ResetPointed = _resetPointed,
            ClearPointed = _clearPointed,
            AutoResetDisabled = !Settings.AutoResetEnabled,
            CountdownStopped = _countdownStopped,
            ConfirmClear = _confirmOpen,
            ConfirmPointed = _confirmPointed,
            HoverRow = _hasHoverRow ? RowRect(_hoverRow) : RectangleF.Empty,
            SelectedRow = selected,
            Popup = _popupEventId is not null ? _popupRect : RectangleF.Empty,
            PopupCurrent = current,
            PopupPointed = _pointedChoice >= 0 && _pointedChoice < InstanceMarks.Choices.Count ? InstanceMarks.Choices[_pointedChoice] : InstanceMark.None,
            PopupReturnAction = ReturnAction,
            PopupReturnEnabled = PopupReturnEnabled,
            PopupReturnPointed = _pointedChoice == PanelGeometry.ReturnChoiceIndex,
        };
    }

    // ------------------------------------------------------------------ 描画

    /// <summary>ウィンドウの中身を1枚に描く。<paramref name="graphics"/> は中身の大きさの面。</summary>
    public void Render(Graphics graphics)
    {
        EnsureLayout();

        // 初回起動の案内は、地も自分で塗る。
        if (!_onboarding.WelcomeOpen)
        {
            graphics.Clear(UiMetrics.WindowBackground);
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.SmoothingMode = SmoothingMode.None;
        }

        if (_onboarding.IsOpen)
        {
            _onboarding.Render(graphics, _client);
            Dirty = false;
            return;
        }

        RenderPanel(graphics);
        _statusBar.Render(graphics);
        RenderTabs(graphics);

        if (_tab == DesktopTab.Details)
            _rowDetails.Render(graphics);
        else
            RenderSettings(graphics);

        // 状態の詳しい文はパネルの上に重ねるので、最後に描く。
        _statusBar.RenderDetail(graphics);

        Dirty = false;
    }

    /// <summary>
    /// 点滅する点だけを描き直す（→実装メモ5.91）。描き直した範囲（画素）を返す。
    /// 並べ直しの前・全体の描き直しが要るとき・点滅する点がないときは null（そのときは全体を描き直す）。
    /// </summary>
    public Rectangle? RenderBlinkingDots(Graphics graphics)
    {
        // 初回起動の案内を出している間は、状態の段が見えていない。
        if (_layoutDirty || Dirty || _onboarding.IsOpen)
            return null;

        return _statusBar.RenderBlinkingDots(graphics);
    }

    /// <summary>
    /// 右側の設定。見えている範囲で切り、送れるときは列の右の余白に細いつまみを出す（パネルのつまみと同じ色→実装メモ5.67）。
    /// </summary>
    private void RenderSettings(Graphics graphics)
    {
        var state = graphics.Save();
        graphics.SetClip(_settingsViewport);
        _settingsView.Render(graphics);
        graphics.Restore(state);

        if (_settingsScrollMax <= 0f)
            return;

        var track = new RectangleF(_settingsViewport.Right + S(5f), _settingsViewport.Y, MathF.Max(2f, S(4f)), _settingsViewport.Height);
        var content = _settingsViewport.Height + _settingsScrollMax;
        var thumbHeight = MathF.Max(S(24f), track.Height * (_settingsViewport.Height / content));
        var thumbTop = track.Y + ((track.Height - thumbHeight) * (_settingsScroll / _settingsScrollMax));

        graphics.FillRectangle(_painter.Brush(Color.FromArgb(90, _style.ScrollTrack)), track);
        graphics.FillRectangle(_painter.Brush(_style.ScrollTrack), track.X, thumbTop, track.Width, thumbHeight);
    }

    /// <summary>右側のタブ。いま出しているタブは見出しの色で塗り、下にアクセント色の線を引く。</summary>
    private void RenderTabs(Graphics graphics)
    {
        var font = _painter.Fonts.ResetLabel;
        var underline = MathF.Max(2f, MathF.Round(S(2f)));

        for (var i = 0; i < Tabs.Length; i++)
        {
            var rect = _tabRects[i];
            var active = Tabs[i].Tab == _tab;
            var pointed = i == _tabPointed && !active;

            graphics.FillRectangle(_painter.Brush(active ? _style.Header : pointed ? _style.CurrentRow : _style.Surface), rect);

            if (active)
                graphics.FillRectangle(_painter.Brush(_style.Accent), rect.X, rect.Bottom - underline, rect.Width, underline);

            var label = Tabs[i].Label;
            var width = _painter.MeasureWidth(label, font);
            var brush = _painter.Brush(active || pointed ? _style.Text : _style.Muted);
            graphics.DrawString(label, font, brush, rect.X + ((rect.Width - width) / 2f), rect.Y + ((rect.Height - font.GetHeight(graphics)) / 2f), _painter.Format);
        }
    }

    private void RenderPanel(Graphics graphics)
    {
        if (_rowsDirty)
        {
            _panel.RenderRows(_layouts);
            _rowsDirty = false;
        }

        _panel.Compose(_scroll.Offset, BuildDecorations());

        var panelRect = CurrentPanelRect();

        var state = graphics.Save();
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;

        graphics.DrawImage(
            _panel.Bitmap,
            panelRect,
            new RectangleF(0f, 0f, _style.Width, _panel.Height),
            GraphicsUnit.Pixel);

        // 行がないときの「該当する履歴はありません」は、手首のパネルと同じく PanelRenderer が絵の中に描く（→実装メモ5.128）。
        graphics.Restore(state);
    }

    public void Dispose()
    {
        _onboarding.Dispose();
        _settingsView.Dispose();
        _painter.Dispose();
        _panel.Dispose();
    }
}
