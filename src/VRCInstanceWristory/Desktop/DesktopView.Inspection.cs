using System.Drawing;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 中を覗くためのもの。ウィンドウ（<see cref="DesktopWindow"/>）は使わず、自動検証と <c>--render-sample --window</c>・<c>--welcome</c> が、
/// 部品の場所を知ってマウスの操作をまねたり、開いている画面を確かめたりするのに使う。
/// 並べる前に聞かれることがあるので、場所を返すものは先に並べる。
/// </summary>
public sealed partial class DesktopView
{
    /// <summary>点滅の時計（→実装メモ5.91）。見本の画像と自動検証では 0 に固定して、点いた状態で描く。</summary>
    public Func<TimeSpan> BlinkClock
    {
        get => _statusBar.BlinkClock;
        set => _statusBar.BlinkClock = value;
    }

    /// <summary>いまの見た目を画像にする。</summary>
    public Bitmap RenderToBitmap()
    {
        var bitmap = new Bitmap(Math.Max(1, _client.Width), Math.Max(1, _client.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(bitmap))
            Render(graphics);

        return bitmap;
    }

    /// <summary>いま出している右側のタブ。</summary>
    public DesktopTab Tab => _tab;

    /// <summary>
    /// 初回起動の案内（<see cref="WelcomeView"/>）を中身の代わりに全面へ出しているか（2026-09-30のユーザー指定）。
    /// 出している間は、「わかった」のほかは押しても何もしない。
    /// </summary>
    public bool WelcomeOpen => _onboarding.WelcomeOpen;

    /// <summary>初期設定の画面を中身の代わりに全面へ出しているか（2026-09-30のユーザー指定→実装メモ5.98）。</summary>
    public bool SetupOpen => _onboarding.SetupOpen;

    /// <summary>「わかった」の矩形（中身の座標）。</summary>
    public RectangleF WelcomeButtonRect() => OnboardingView.WelcomeButtonRect(_client);

    /// <summary>「はじめる」の矩形（中身の座標）。初期設定の画面を出していなければ空。</summary>
    public RectangleF SetupButtonRect()
    {
        EnsureLayout();
        return _onboarding.SetupButton;
    }

    /// <summary>目印のポップアップを開いているか。</summary>
    public bool MarkPopupOpen => _popupEventId is not null;

    /// <summary>履歴リセットの確認を出しているか（→実装メモ5.65）。</summary>
    public bool ClearConfirmOpen => _confirmOpen;

    /// <summary>選んだ行のeventId。選んでいなければ null。</summary>
    public string? SelectedEventId => _selectedEventId;

    /// <summary>いま見ているスクロール位置（パネル内のpx）。表示領域の高さは並べてから決まるので、先に並べる。</summary>
    public float ScrollOffset
    {
        get
        {
            EnsureLayout();
            return _scroll.Offset;
        }
    }

    /// <summary>画面上のパネルの矩形。</summary>
    public RectangleF PanelRect
    {
        get
        {
            EnsureLayout();
            return CurrentPanelRect();
        }
    }

    /// <summary>
    /// <paramref name="index"/> 番目の行の、画面上の見えている矩形。帯のぶんは含めない。見えていなければ高さ0。
    /// 行は上に寄せて並ぶので、行の位置は行数とスクロールで変わる（→実装メモ5.71）。
    /// </summary>
    public RectangleF RowScreenRect(int index)
    {
        EnsureLayout();
        return ToScreen(RowRect(_panel.RowAt(_layouts, index)));
    }

    /// <summary>目印の選択肢の、画面上の矩形。ポップアップを開いていなければ null。</summary>
    public RectangleF? MarkChoiceRect(int index)
        => _popupEventId is null ? null : ToScreen(PanelGeometry.MarkChoiceRect(_popupRect, index));

    /// <summary>ポップアップの「ここへ戻る」の、画面上の矩形。ポップアップを開いていなければ null。</summary>
    public RectangleF? ReturnChoiceRect()
        => _popupEventId is null ? null : ToScreen(PanelGeometry.ReturnChoiceRect(_popupRect));

    /// <summary>見出しの残り時間の数字の、画面上の矩形（→実装メモ5.130）。</summary>
    public RectangleF CountdownScreenRect() => ToScreen(_panel.CountdownRectFor(HeaderState()));

    /// <summary>見出しの「延長」の、画面上の矩形。</summary>
    public RectangleF ResetButtonScreenRect() => ToScreen(_panel.ResetButtonRectFor(HeaderState()));

    /// <summary>見出しの「リセット」の、画面上の矩形。</summary>
    public RectangleF ClearButtonScreenRect() => ToScreen(_panel.ClearButtonRectFor(HeaderState()));

    /// <summary>
    /// 履歴リセットの確認のボタンの、画面上の矩形。確認を出していなければ null。
    /// <paramref name="which"/> は <see cref="PanelGeometry.ConfirmCancel"/> か <see cref="PanelGeometry.ConfirmAccept"/>。
    /// </summary>
    public RectangleF? ConfirmButtonRect(int which)
        => _confirmOpen ? ToScreen(PanelGeometry.ConfirmButtonRect(ConfirmBox, which)) : null;

    /// <summary>右側のタブの矩形。</summary>
    public RectangleF TabRect(DesktopTab tab)
    {
        EnsureLayout();
        return _tabRects[Array.FindIndex(Tabs, t => t.Tab == tab)];
    }

    /// <summary>設定を送れる量（論理pxではなく画素）。0なら入り切っている。</summary>
    public float SettingsScrollMax
    {
        get
        {
            EnsureLayout();
            return _settingsScrollMax;
        }
    }

    /// <summary>いまの設定の送り位置（画素）。</summary>
    public float SettingsScroll => _settingsScroll;

    /// <summary>設定の部品が出している吹き出しの文字。出していなければ null。</summary>
    public string? SettingsHint => _tab == DesktopTab.Details ? null : _settingsView.HintText;

    /// <summary>設定の値を表示に使う書式。<paramref name="index"/> は <see cref="SettingsStepper"/> の番号。</summary>
    public string StepperText(int index) => _settingsView.StepperText(index);

    /// <summary>設定の部品の矩形。その部品がいまのタブ（か初期設定の画面）に出ていなければ null。</summary>
    public RectangleF? TargetRect(SettingsView.HitKind kind, int index = 0)
    {
        EnsureLayout();
        return _tab == DesktopTab.Details && !_onboarding.SetupOpen ? null : _settingsView.TargetRect(kind, index);
    }

    /// <summary>「選んだ行」の部品の矩形。インスタンス詳細を出していなければ null。</summary>
    public RectangleF? DetailsTargetRect(RowDetailsView.HitKind kind, int index = 0)
    {
        EnsureLayout();
        return _detailsRect.IsEmpty ? null : _rowDetails.TargetRect(kind, index);
    }

    /// <summary>状態の段の項目。</summary>
    public IReadOnlyList<StatusItem> StatusItems => _statusBar.Items;

    /// <summary>状態の段の項目の矩形。</summary>
    public RectangleF StatusSlot(int index)
    {
        EnsureLayout();
        return _statusBar.Slot(index);
    }

    /// <summary>状態の詳しい文を出しているか。出していれば、その項目の番号。</summary>
    public int? PointedStatus => _statusBar.PointedDetail;

    /// <summary>開発者のリンクの矩形。</summary>
    public RectangleF CreditLinkRect
    {
        get
        {
            EnsureLayout();
            return _statusBar.CreditLinkRect;
        }
    }

    /// <summary>新しい版を知らせるリンクの矩形（→実装メモ5.121）。出していなければ空。</summary>
    public RectangleF UpdateLinkRect
    {
        get
        {
            EnsureLayout();
            return _statusBar.UpdateLinkRect;
        }
    }

    /// <summary>新しい版を知らせるリンクの文字。出していなければ null。</summary>
    public string? UpdateLinkText => _statusBar.UpdateLinkText;
}
