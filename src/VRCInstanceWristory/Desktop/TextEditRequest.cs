using System.Drawing;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// グループ名を打ち込む欄を開いてほしい、という依頼（→実装メモ5.48）。
/// 欄そのもの（Win32 の EDIT）はウィンドウが作る。ここにあるのは置き場所と最初の文字だけ。
/// </summary>
/// <param name="Rect">欄を置く矩形（ウィンドウの中の画素）。</param>
/// <param name="FontPixels">欄の文字の大きさ（画素）。まわりの文字にそろえる。</param>
/// <param name="FontFamily">欄の文字のフォント（日本語のフォント）。</param>
public sealed record TextEditRequest(RectangleF Rect, string Initial, string GroupId, float FontPixels, string FontFamily);
