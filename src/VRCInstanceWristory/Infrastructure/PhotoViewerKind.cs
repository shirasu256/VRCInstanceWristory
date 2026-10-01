namespace VRCInstanceWristory.Infrastructure;

/// <summary>写真のサムネイルを押したときに開くアプリ（設定 <c>photoViewer</c>・2026-09-27のユーザー指定→実装メモ5.55）。</summary>
public enum PhotoViewerKind
{
    /// <summary>既定。その種類のファイルの既定のアプリ（エクスプローラーでダブルクリックしたときと同じ）。</summary>
    Default,

    /// <summary>利用者が選んだ実行ファイル（設定 <c>photoViewerPath</c>）。</summary>
    Custom,
}

/// <summary><see cref="PhotoViewerKind"/> の設定の名前と表示名。</summary>
public static class PhotoViewers
{
    public static string SettingName(PhotoViewerKind kind) => kind switch
    {
        PhotoViewerKind.Custom => "custom",
        _ => "default",
    };

    /// <summary>設定の名前を読む（大文字小文字は問わない）。知らない名前なら null。</summary>
    public static PhotoViewerKind? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "default" => PhotoViewerKind.Default,
        "custom" => PhotoViewerKind.Custom,
        _ => null,
    };

    /// <summary>設定の画面に出す名前。</summary>
    public static string DisplayName(PhotoViewerKind kind) => kind switch
    {
        PhotoViewerKind.Custom => "アプリを選択",
        _ => "既定",
    };
}
