namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// 保存先の場所。どれも <c>%LocalAppData%\VRCInstanceWristory</c>（<see cref="DataDirectory"/>）の中に置く。
///
/// | 場所 | 中身 |
/// | --- | --- |
/// | <see cref="Settings"/> | 設定（<see cref="AppSettings"/>） |
/// | <see cref="Checkpoint"/> | 回数のチェックポイント（ソースの台帳・消去の区切りなど） |
/// | <see cref="History"/> | 訪問履歴の行（→実装メモ5.108） |
/// | <see cref="Marks"/> | 目印（→実装メモ5.44・5.54） |
/// | <see cref="ExternalReset"/> | 外部からの履歴リセットを最後に受け付けた時刻（→実装メモ5.83） |
/// | <see cref="Thumbnails"/> | 写真のサムネイル（→実装メモ5.55） |
/// | <see cref="AppLog"/> | 画面に出ないときの出力（→実装メモ5.63） |
/// </summary>
public static class AppPaths
{
    /// <summary>いまの利用者の <c>%LocalAppData%</c>。</summary>
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DataDirectory => Path.Combine(LocalAppData, AppInfo.InternalName);

    public static string Settings => Path.Combine(DataDirectory, "settings.json");

    public static string Checkpoint => Path.Combine(DataDirectory, "counter-state.json");

    public static string Marks => Path.Combine(DataDirectory, "marks.json");

    /// <summary>自動リセットが無効のときは、起動時にここから行を戻す（ログは24時間で消えるため）。</summary>
    public static string History => Path.Combine(DataDirectory, "history.json");

    /// <summary>設定ではないので settings.json とは分ける。</summary>
    public static string ExternalReset => Path.Combine(DataDirectory, "external-reset.json");

    /// <summary>ログのリセットで、消えた訪問のものは消す。</summary>
    public static string Thumbnails => Path.Combine(DataDirectory, "thumbnails");

    /// <summary>起動のたびに作り直す。</summary>
    public static string AppLog => Path.Combine(DataDirectory, "app.log");
}
