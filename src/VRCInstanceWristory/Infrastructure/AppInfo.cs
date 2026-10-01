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

    /// <summary>「v0.1」の形。省略できる修正版番号（0）は出さない。</summary>
    public static string ShortVersion { get; } = ReadShortVersion();

    /// <summary>「VRC Instance Wristory v0.1」。</summary>
    public static string NameWithVersion { get; } = $"{DisplayName} {ShortVersion}";

    private static string ReadShortVersion()
    {
        var assembly = typeof(AppInfo).Assembly;

        // InformationalVersion は "0.1.0+<commit>" の形になることがあるので、+ 以降を落とす。
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = text?.IndexOf('+') ?? -1;

        if (plus >= 0)
            text = text![..plus];

        if (!Version.TryParse(text, out var version))
            version = assembly.GetName().Version ?? new Version(0, 0);

        var build = version.Build > 0
            ? "." + version.Build.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"v{version.Major}.{version.Minor}{build}");
    }
}
