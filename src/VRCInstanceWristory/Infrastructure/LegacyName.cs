namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// 旧名（InstanceIDLogger）からの引き継ぎ（2026-09-28に正式名称を「VRC Instance Wristory」へ決めた→実装メモ5.84）。
///
/// 名前を変えると、保存先のフォルダー・スタートアップの登録・SteamVRの登録がどれも別物になる。
/// 旧名のものが残っていたら、初めて起動したときに新しい名前へ移す。
///
/// | 旧名のもの | 引き継ぎ方 |
/// | --- | --- |
/// | <c>%LocalAppData%\InstanceIDLogger</c> | 新しいフォルダーがまだなければ、フォルダーごと移す（<see cref="MoveDataDirectory"/>） |
/// | Run キーの <c>InstanceIDLogger</c> | 有効だったら新しい名前で登録し直し、旧名の値は消す（<see cref="StartupRegistration.MigrateLegacy"/>） |
/// | SteamVRの <c>instanceidlogger.overlay</c> | 自動起動だったら新しいキーで自動起動にし、旧名の登録は外す（<c>SteamVrSession.RegisterApplicationManifest</c>） |
/// | Mutex <c>Local\InstanceIDLogger.v1</c> | 旧版が起動中なら、同じ手首に2枚出さないよう起動しない（<c>LiveMode</c>） |
/// </summary>
public static class LegacyName
{
    /// <summary>旧名。フォルダー名・Run キーの値の名前・実行ファイル名に使っていた。</summary>
    public const string Name = "InstanceIDLogger";

    /// <summary>旧版が持つ Mutex の名前。</summary>
    public const string MutexName = @"Local\InstanceIDLogger.v1";

    /// <summary>旧版がSteamVRへ登録していたキー。</summary>
    public const string SteamVrApplicationKey = "instanceidlogger.overlay";

    /// <summary>旧版の manifest のファイル名（実行ファイルの隣の Resources にある）。</summary>
    public const string ManifestFileName = "instanceidlogger.vrmanifest";

    /// <summary>旧版の保存先。</summary>
    public static string DefaultDirectory => Path.Combine(AppPaths.LocalAppData, Name);

    /// <summary>
    /// 旧版の保存先を新しい保存先へ移す。新しい方がまだなく、旧い方があるときだけ移す
    /// （新しい方がすでにあれば、そちらを正とし何もしない）。
    /// 移した・移せなかったときは、その旨を返す（何もしなかったときは null）。
    /// </summary>
    public static string? MoveDataDirectory(string legacyDirectory, string currentDirectory)
    {
        try
        {
            if (Directory.Exists(currentDirectory) || !Directory.Exists(legacyDirectory))
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(currentDirectory))!);
            Directory.Move(legacyDirectory, currentDirectory);
            return $"旧名の保存先を引き継ぎました: {legacyDirectory} → {currentDirectory}";
        }
        catch (Exception ex)
        {
            return $"旧名の保存先を引き継げません（{legacyDirectory}）: {ex.Message}";
        }
    }
}
