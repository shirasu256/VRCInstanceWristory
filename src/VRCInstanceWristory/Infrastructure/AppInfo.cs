using System.Globalization;
using System.Reflection;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// 画面へ出すアプリ名とバージョン。バージョンはアセンブリの値をそのまま使うので、
/// Directory.Build.props の &lt;Version&gt; を上げれば表示も追随する。
/// </summary>
public static class AppInfo
{
    /// <summary>パネル下部などに出す表示名。</summary>
    public const string DisplayName = "VRC Instance Wristory";

    /// <summary>
    /// 空白を含まない内部の名前。実行ファイル・保存先のフォルダー・パイプ・スタートアップの登録などの名前の元にする。
    /// 変えると、どれも別物になって引き継げなくなる（→実装メモ5.84）。
    /// </summary>
    public const string InternalName = "VRCInstanceWristory";

    /// <summary>ステータスの段の右端の「shirasu256」から開く、開発者のページ（→実装メモ5.71・5.72）。</summary>
    public const string DeveloperPage = "https://5rs.uk/0";

    /// <summary>公開するリポジトリ。アップデートはここの Releases から探す（→実装メモ5.121）。</summary>
    public const string RepositoryUrl = "https://github.com/shirasu256/VRCInstanceWristory";

    /// <summary>
    /// インストーラー（Velopack）の packId（→実装メモ5.121）。インストール先は <c>%LocalAppData%\VRCInstanceWristoryApp</c>。
    /// 保存先（<see cref="InternalName"/> のフォルダー）と同じ名前にしない。Velopack はインストールし直すとき・アンインストールするときに
    /// インストール先をまるごと消すので、同じ名前にすると設定と訪問履歴まで消える。
    /// </summary>
    public const string PackageId = "VRCInstanceWristoryApp";

    /// <summary>
    /// 「v0.1.0」の形。末尾が0でも省かない（2026-10-01のユーザー指定→実装メモ5.123）。
    /// GitHub の Releases のタグ（<c>v0.1.0</c>）と同じ書き方にそろえる。
    /// </summary>
    public static string DisplayVersion { get; } = ReadDisplayVersion();

    /// <summary>「VRC Instance Wristory v0.1.0」。</summary>
    public static string NameWithVersion { get; } = $"{DisplayName} {DisplayVersion}";

    private static string ReadDisplayVersion()
    {
        var assembly = typeof(AppInfo).Assembly;

        // InformationalVersion は "0.1.0+<commit>" の形になることがあるので、+ 以降を落とす。
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = text?.IndexOf('+') ?? -1;

        if (plus >= 0)
            text = text![..plus];

        if (!Version.TryParse(text, out var version))
            version = assembly.GetName().Version ?? new Version(0, 0);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"v{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}");
    }
}
