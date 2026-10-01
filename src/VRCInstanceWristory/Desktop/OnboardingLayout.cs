using System.Drawing;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 初回起動の案内（<see cref="WelcomeView"/>）と初期設定の画面（<see cref="OnboardingView"/>）に共通の縦の並び
/// （2026-09-30のユーザー指定→実装メモ5.100）。見出し → 中身 → 「わかった」／「はじめる」を縦に並べ、まとまりごと縦の中央に置く。
/// 中身の幅は初期設定の2列の幅にそろえる。寸法は論理px（96dpi）。
/// </summary>
internal static class OnboardingLayout
{
    /// <summary>見出し（案内の上の説明・「初期設定」）の字の大きさ。</summary>
    public const float TitleSize = 24f * 1.3f;

    /// <summary>見出しと中身の間。</summary>
    public const float TitleGap = 44f;

    /// <summary>中身とボタンの間。</summary>
    public const float ButtonGap = 48f;

    /// <summary>まとまりの上下に最低限空ける幅。</summary>
    public const float VerticalMargin = 36f;

    /// <summary>「わかった」「はじめる」の大きさ（ほかのボタンの1.5倍→2026-09-30のユーザー指定）。枠・字とも1.5倍。</summary>
    public static readonly SizeF ButtonSize = new(300f, 60f);

    /// <summary>ボタンの字の倍率。</summary>
    public const float ButtonScale = 1.5f;

    /// <summary>中身の幅（初期設定の2列の幅）。</summary>
    public const float ContentWidth = (SettingsView.ColumnWidth * 2f) + SettingsView.ColumnGap;
}
