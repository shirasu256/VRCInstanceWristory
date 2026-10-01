using System.Drawing;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 初回起動の案内（<see cref="WelcomeView"/>）と、そのあとの初期設定の画面（2026-09-30のユーザー指定→実装メモ5.97・5.98）。
/// どちらもデスクトップのウィンドウの中身の代わりに全面へ出す。
///
/// 案内の「わかった」で初期設定の画面へ進み、「はじめる」で閉じて <see cref="DesktopCommand.FinishWelcome"/> を出す。
/// 初期設定の部品は右側のタブと同じ <see cref="SettingsView"/> で、<see cref="SettingsSections.Setup"/> のまとまりを2列に並べる。
/// 出している間、ほかの部品は押しても何もしない（<see cref="DesktopView"/> が先にこちらへ渡す）。
/// </summary>
internal sealed class OnboardingView : IDisposable
{
    /// <summary>初期設定の画面の見出し（2026-09-30のユーザー指定→実装メモ5.98）。</summary>
    private const string SetupTitle = "初期設定";

    /// <summary>初期設定の画面を閉じるボタン。</summary>
    private const string SetupButtonLabel = "はじめる";

    /// <summary>設定が入り切らないときの左右の余白（論理px）。</summary>
    private const float SideMargin = 16f;

    /// <summary>入り切らないときに縮める倍率の下限。</summary>
    private const float MinScale = 0.3f;

    private readonly PanelStyle _style;
    private readonly SettingsView _settingsView;
    private readonly Action<DesktopCommand> _emit;

    // 初回起動の案内（出している間だけある）。
    private WelcomeView? _welcome;
    private bool _welcomePointed;

    // 初期設定の画面。描き手・見出しの字は、並べた倍率で作って持っておく。
    private bool _setupOpen;
    private bool _setupPointed;
    private float _setupScale;
    private RectangleF _setupButton;
    private float _setupTitleY;
    private UiPainter? _setupPainter;
    private UiPainter? _setupButtonPainter;
    private Font? _setupTitleFont;

    private bool _dirty = true;

    public OnboardingView(PanelStyle style, SettingsView settingsView, Action<DesktopCommand> emit)
    {
        _style = style;
        _settingsView = settingsView;
        _emit = emit;
    }

    /// <summary>押した結果。ウィンドウの中身の並びが変わるなら、置く側が並べ直す。</summary>
    public enum PressResult
    {
        None,

        /// <summary>「わかった」を押して初期設定の画面へ進んだ。</summary>
        SetupOpened,

        /// <summary>「はじめる」を押して閉じた（ふだんの画面へ戻る）。</summary>
        Finished,
    }

    public bool WelcomeOpen => _welcome is not null;

    public bool SetupOpen => _setupOpen;

    public bool IsOpen => WelcomeOpen || _setupOpen;

    /// <summary>
    /// 描き直しが必要か。案内の間は「わかった」を指したかどうかだけで決める（見えていない設定の部品の印は見ない）。
    /// 初期設定の画面では、設定の部品の印も見る。
    /// </summary>
    public bool Dirty => _dirty || (_setupOpen && _settingsView.Dirty);

    /// <summary>初期設定の画面の「はじめる」の矩形（中身の座標）。出していなければ空。</summary>
    public RectangleF SetupButton => _setupOpen ? _setupButton : RectangleF.Empty;

    /// <summary>「わかった」の矩形（中身の座標）。</summary>
    public static RectangleF WelcomeButtonRect(Size client) => WelcomeView.ButtonRect(client);

    /// <summary>初回起動の案内を出す。</summary>
    public void ShowWelcome()
    {
        _welcome ??= new WelcomeView(_style);
        _welcomePointed = false;
        _setupOpen = false;
        _dirty = true;
    }

    /// <summary>
    /// 初期設定の画面を並べる。見出し・設定（2列）・「はじめる」を縦に並べ、表示倍率で組んで、入り切らなければ全体を縮める。
    /// 案内を出している間は何もしない。
    /// </summary>
    public void Layout(Size client, float scale)
    {
        if (!_setupOpen)
            return;

        var width = client.Width;
        var height = client.Height;
        var fixedHeight = OnboardingLayout.VerticalMargin + OnboardingLayout.TitleSize + OnboardingLayout.TitleGap
            + OnboardingLayout.ButtonGap + OnboardingLayout.ButtonSize.Height + OnboardingLayout.VerticalMargin;

        // 設定の高さは倍率にほぼ比例するので、1回並べて測り、入り切らなければその比で縮めて並べ直す。
        var size = _settingsView.Layout(PointF.Empty, scale, columns: 2, SettingsSections.Setup);
        var fit = MathF.Min(width / (size.Width + (SideMargin * 2f * scale)), height / (size.Height + (fixedHeight * scale)));

        if (fit < 1f)
        {
            scale = MathF.Max(MinScale, scale * fit);
            size = _settingsView.Layout(PointF.Empty, scale, columns: 2, SettingsSections.Setup);
        }

        var total = size.Height + (fixedHeight * scale);
        var top = MathF.Max(0f, (height - total) / 2f);

        _setupTitleY = top + (OnboardingLayout.VerticalMargin * scale);
        var settingsTop = _setupTitleY + ((OnboardingLayout.TitleSize + OnboardingLayout.TitleGap) * scale);
        size = _settingsView.Layout(new PointF((width - size.Width) / 2f, settingsTop), scale, columns: 2, SettingsSections.Setup);

        var button = new SizeF(OnboardingLayout.ButtonSize.Width * scale, OnboardingLayout.ButtonSize.Height * scale);
        _setupButton = new RectangleF((width - button.Width) / 2f, settingsTop + size.Height + (OnboardingLayout.ButtonGap * scale), button.Width, button.Height);

        if (_setupPainter is null || _setupScale != scale)
        {
            DisposeSetupResources();
            _setupPainter = new UiPainter(_style, scale);
            _setupButtonPainter = new UiPainter(_style, scale * OnboardingLayout.ButtonScale);
            _setupTitleFont = new Font(_setupPainter.Fonts.JapaneseFamilyName, OnboardingLayout.TitleSize * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            _setupScale = scale;
        }

        _dirty = true;
    }

    /// <summary>マウスの位置（null なら外れた）。</summary>
    public void PointerMove(PointF? mouse, Size client)
    {
        if (WelcomeOpen)
        {
            SetPointed(ref _welcomePointed, mouse is { } m && WelcomeButtonRect(client).Contains(m));
            return;
        }

        _settingsView.PointerMove(mouse is { } sm && _settingsView.Bounds.Contains(sm) ? sm : null);
        SetPointed(ref _setupPointed, mouse is { } bm && _setupButton.Contains(bm));
    }

    private void SetPointed(ref bool field, bool pointed)
    {
        if (pointed == field)
            return;

        field = pointed;
        _dirty = true;
    }

    /// <summary>左ボタン。案内は「わかった」だけ、初期設定の画面は設定の部品と「はじめる」だけを受け付ける。</summary>
    public PressResult Press(PointF point, Size client)
    {
        if (WelcomeOpen)
        {
            if (!WelcomeButtonRect(client).Contains(point))
                return PressResult.None;

            _welcome?.Dispose();
            _welcome = null;
            _welcomePointed = false;
            _setupOpen = true;
            _dirty = true;
            return PressResult.SetupOpened;
        }

        if (!_setupOpen)
            return PressResult.None;

        if (_setupButton.Contains(point))
        {
            _setupOpen = false;
            _setupPointed = false;
            _settingsView.PointerMove(null);
            _emit(new DesktopCommand.FinishWelcome());
            DisposeSetupResources();
            return PressResult.Finished;
        }

        if (_settingsView.Bounds.Contains(point))
            _settingsView.PointerDown(point);

        return PressResult.None;
    }

    /// <summary>ホイール。初期設定の画面では、−／＋ の行の上でだけ値を動かす（画面は送らない）。</summary>
    public void Wheel(PointF point, int delta)
    {
        if (_setupOpen && _settingsView.Bounds.Contains(point))
            _settingsView.Wheel(point, delta);
    }

    /// <summary>その位置が押せる部品の上か。</summary>
    public bool IsClickable(PointF point, Size client)
    {
        if (WelcomeOpen)
            return WelcomeButtonRect(client).Contains(point);

        return _setupOpen && (_setupButton.Contains(point) || _settingsView.IsClickable(point));
    }

    /// <summary>案内か初期設定の画面を描く。初期設定の画面の地は、置く側が塗ってから呼ぶ。</summary>
    public void Render(Graphics graphics, Size client)
    {
        if (_welcome is { } welcome)
        {
            welcome.Draw(graphics, client, _welcomePointed);
        }
        else if (_setupOpen)
        {
            var painter = _setupPainter!;
            var title = _setupTitleFont!;
            var x = (client.Width - painter.MeasureWidth(SetupTitle, title)) / 2f;
            graphics.DrawString(SetupTitle, title, painter.Brush(_style.Text), x, _setupTitleY, painter.Format);

            _settingsView.Render(graphics);
            _setupButtonPainter!.DrawButton(graphics, _setupButton, SetupButtonLabel, _setupPointed);
        }

        _dirty = false;
    }

    private void DisposeSetupResources()
    {
        _setupTitleFont?.Dispose();
        _setupButtonPainter?.Dispose();
        _setupPainter?.Dispose();
        _setupTitleFont = null;
        _setupButtonPainter = null;
        _setupPainter = null;
    }

    public void Dispose()
    {
        _welcome?.Dispose();
        DisposeSetupResources();
    }
}
