using System.Drawing;
using VRCInstanceWristory.Core.Presentation;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 1行の実測レイアウト。長いIDは縮小・折り返しで全文を保つ。
/// <see cref="Height"/> には、この行の上に入れる帯（クラッシュ・「対象外のインスタンスへ移動」）も含む。
/// </summary>
public sealed record RowLayout(DisplayRow Row, Font IdFont, IReadOnlyList<string> IdLines, float Height);
