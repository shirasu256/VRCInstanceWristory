namespace VRCInstanceWristory.Tests;

/// <summary>
/// 検証が読むリポジトリの中のファイルの場所。検証の実行フォルダー（<c>bin\...</c>）から上へたどって探す。
/// </summary>
public static class TestPaths
{
    private static readonly Lazy<string> Root = new(FindRepositoryRoot);

    /// <summary>リポジトリの一番上（<c>VRCInstanceWristory.slnx</c> のあるフォルダー）。</summary>
    public static string RepositoryRoot => Root.Value;

    /// <summary>アプリのプロジェクト（<c>src\VRCInstanceWristory</c>）。</summary>
    public static string AppProject => Path.Combine(RepositoryRoot, "src", "VRCInstanceWristory");

    /// <summary>アプリに同梱する資料（<c>src\VRCInstanceWristory\Resources</c>）。</summary>
    public static string Resources => Path.Combine(AppProject, "Resources");

    /// <summary>検証の実行フォルダーへ写した検証用の資料（<c>Fixtures</c>）。</summary>
    public static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VRCInstanceWristory.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException($"{AppContext.BaseDirectory} の上に VRCInstanceWristory.slnx が見つかりません。");
    }
}
