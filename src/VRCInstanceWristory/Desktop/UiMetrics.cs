using System.Drawing;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// デスクトップのウィンドウとSteamVRのダッシュボードの部品に共通の寸法と色（→実装メモ5.39・5.42）。
/// 設定のまとまり（<see cref="SettingsView"/>）と「インスタンス詳細」の枠（<see cref="RowDetailsView"/>）は同じ見た目
/// （見出しの帯と本文の2段）にしてあるので、枠の寸法はここで1か所に決める。寸法は論理px（96dpi）。
/// </summary>
internal static class UiMetrics
{
    /// <summary>ウィンドウの地の色（初回起動の案内・初期設定の画面も同じ）。</summary>
    public static readonly Color WindowBackground = Color.FromArgb(0x0c, 0x11, 0x15);

    /// <summary>枠と枠の間。</summary>
    public const float CardGap = 12f;

    /// <summary>枠の見出しの帯の高さ。</summary>
    public const float CardTitleHeight = 28f;

    /// <summary>枠の中の左右と下の余白。</summary>
    public const float CardPadding = 12f;

    /// <summary>説明・注記の1行の高さ。</summary>
    public const float NoteLineHeight = 17f;
}
