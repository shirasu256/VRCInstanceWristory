namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 予告のアイコンを置く場所（VRChatのマイクのアイコンから見た向き→実装メモ5.87・5.89）。
/// 設定の「表示位置」の ‹ › は <see cref="ResetWarningPositions.Order"/> の順に巡る。
/// </summary>
public enum ResetWarningPosition
{
    BottomLeft,
    Left,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
}

public static class ResetWarningPositions
{
    /// <summary>
    /// ‹ › で巡る順（2026-09-29のユーザー指定→実装メモ5.89）。左下から時計回りに1周し、最後の「下」の次は「左下」へ戻る。
    /// </summary>
    public static readonly ResetWarningPosition[] Order =
    [
        ResetWarningPosition.BottomLeft,
        ResetWarningPosition.Left,
        ResetWarningPosition.TopLeft,
        ResetWarningPosition.Top,
        ResetWarningPosition.TopRight,
        ResetWarningPosition.Right,
        ResetWarningPosition.BottomRight,
        ResetWarningPosition.Bottom,
    ];

    public const ResetWarningPosition Default = ResetWarningPosition.BottomLeft;

    /// <summary>画面に出す名前。</summary>
    public static string DisplayName(ResetWarningPosition position) => position switch
    {
        ResetWarningPosition.BottomLeft => "左下",
        ResetWarningPosition.Left => "左",
        ResetWarningPosition.TopLeft => "左上",
        ResetWarningPosition.Top => "上",
        ResetWarningPosition.TopRight => "右上",
        ResetWarningPosition.Right => "右",
        ResetWarningPosition.BottomRight => "右下",
        _ => "下",
    };

    /// <summary>設定ファイルでの名前（<c>bottomLeft</c> など）。</summary>
    public static string SettingName(ResetWarningPosition position)
    {
        var name = position.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static ResetWarningPosition? Parse(string? name)
        => Enum.TryParse<ResetWarningPosition>(name, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : null;

    /// <summary>
    /// マイクのアイコンの中心から見た向き（右と上が正）。左右・上下とも -1 / 0 / +1。
    /// </summary>
    public static (int X, int Y) Direction(ResetWarningPosition position) => position switch
    {
        ResetWarningPosition.BottomLeft => (-1, -1),
        ResetWarningPosition.Left => (-1, 0),
        ResetWarningPosition.TopLeft => (-1, 1),
        ResetWarningPosition.Top => (0, 1),
        ResetWarningPosition.TopRight => (1, 1),
        ResetWarningPosition.Right => (1, 0),
        ResetWarningPosition.BottomRight => (1, -1),
        _ => (0, -1),
    };

    /// <summary>‹ › を1回押したときの次の場所（端で反対側へ巡る）。</summary>
    public static ResetWarningPosition Next(ResetWarningPosition position, int direction)
    {
        var index = Array.IndexOf(Order, position);
        var next = ((index < 0 ? 0 : index) + Math.Sign(direction) + Order.Length) % Order.Length;
        return Order[next];
    }
}
