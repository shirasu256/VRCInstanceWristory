using Microsoft.Win32;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// Windows のスタートアップの登録先（2026-09-26のユーザー指定→実装メモ5.51）。
/// 自動検証で本物のレジストリに触れないよう、読み書きをここに分けてある。
/// </summary>
public interface IStartupRegistry
{
    /// <summary><c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> の値。なければ null。</summary>
    string? GetRunValue(string name);

    void SetRunValue(string name, string command);

    void DeleteRunValue(string name);

    /// <summary>
    /// <c>…\Explorer\StartupApproved\Run</c> の値。タスクマネージャーの「スタートアップ アプリ」で無効にすると、
    /// Run の値は残したままここに印が付く。なければ null。
    /// </summary>
    byte[]? GetApprovedValue(string name);

    void DeleteApprovedValue(string name);
}

/// <summary>いまの利用者（HKCU）のスタートアップ。管理者の権限は要らない。</summary>
public sealed class WindowsStartupRegistry : IStartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public string? GetRunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) as string;
    }

    public void SetRunValue(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void DeleteRunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public byte[]? GetApprovedValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(name) as byte[];
    }

    public void DeleteApprovedValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>
/// Windows のログオン時に起動する登録（2026-09-26のユーザー指定→実装メモ5.51）。
///
/// インストーラーを使わず、いまの利用者のスタートアップ（Run キー）へ「この実行ファイルを <c>--minimized</c> で起動する」
/// という1行を書くだけにする。<c>--minimized</c> ではウィンドウをタスクトレイに入れた状態で始める。
///
/// 正本はレジストリで、設定ファイルには書かない。タスクマネージャーの「スタートアップ アプリ」で無効にされたら、
/// その状態をそのまま「登録していない」として見せる（こちらの設定と食い違わないようにするため）。
/// 別の場所の実行ファイルを指している登録（フォルダーを移した・開発中のビルド）も「登録していない」として扱い、
/// 登録し直すとこの実行ファイルを指すように書き換える。
/// </summary>
public sealed class StartupRegistration(IStartupRegistry registry, string executablePath)
{
    /// <summary>Run キーに書く値の名前。</summary>
    public const string ValueName = AppInfo.InternalName;

    /// <summary>ログオン時の起動に付ける引数（タスクトレイに入れて始める）。</summary>
    public const string MinimizedArgument = "--minimized";

    /// <summary>いまの利用者のスタートアップを使う登録。</summary>
    public static StartupRegistration ForCurrentUser()
        => new(new WindowsStartupRegistry(), Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, $"{AppInfo.InternalName}.exe"));

    public string ExecutablePath { get; } = Path.GetFullPath(executablePath);

    /// <summary>Run キーに書く1行。パスは空白を含み得るので引用符で囲む。</summary>
    public string Command => $"\"{ExecutablePath}\" {MinimizedArgument}";

    /// <summary>この実行ファイルがログオン時に起動するよう登録されていて、無効にされていないか。</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                if (registry.GetRunValue(ValueName) is not { } command)
                    return false;

                if (!string.Equals(ExecutableOf(command), ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    return false;

                return !IsDisabledByUser(registry.GetApprovedValue(ValueName));
            }
            catch (Exception)
            {
                // レジストリを読めなければ「登録していない」と見せる。登録し直せば書けるかどうかが分かり、失敗はそこで知らせる。
                return false;
            }
        }
    }

    /// <summary>登録する・外す。結果として登録されているかを返す（書けなかったときは前の状態のまま）。</summary>
    public bool SetEnabled(bool enabled, IDiagnostics? log = null)
    {
        try
        {
            if (enabled)
            {
                registry.SetRunValue(ValueName, Command);

                // タスクマネージャーで無効にした印が残っていると起動されないので、登録し直すときは外す。
                registry.DeleteApprovedValue(ValueName);
            }
            else
            {
                registry.DeleteRunValue(ValueName);
                registry.DeleteApprovedValue(ValueName);
            }
        }
        catch (Exception ex)
        {
            log?.Error($"スタートアップの登録を変えられません: {ex.Message}");
        }

        return IsEnabled;
    }

    /// <summary>
    /// アンインストールするとき（→実装メモ5.121）。どこの実行ファイルを指していても、このアプリの名前の登録を消す
    /// （フォルダーを移したあとの古い登録も残さない）。消えたか（もともとなかったときも）を返す。
    /// </summary>
    public bool Remove(IDiagnostics? log = null)
    {
        try
        {
            registry.DeleteRunValue(ValueName);
            registry.DeleteApprovedValue(ValueName);
            return registry.GetRunValue(ValueName) is null;
        }
        catch (Exception ex)
        {
            log?.Error($"スタートアップの登録を消せません: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 旧名（<see cref="LegacyName.Name"/>）の登録を引き継ぐ（→実装メモ5.84）。
    /// 旧名の登録が有効だったら（どこの実行ファイルを指していても）この実行ファイルで登録し直し、旧名の値は消す。
    /// 利用者が無効にしていた登録は、消すだけで登録し直さない。引き継いだら true。
    /// </summary>
    public bool MigrateLegacy(IDiagnostics? log = null)
    {
        try
        {
            if (registry.GetRunValue(LegacyName.Name) is null)
                return false;

            var enabled = !IsDisabledByUser(registry.GetApprovedValue(LegacyName.Name));

            registry.DeleteRunValue(LegacyName.Name);
            registry.DeleteApprovedValue(LegacyName.Name);

            if (enabled)
                SetEnabled(true, log);

            log?.Info($"旧名のスタートアップの登録を引き継ぎました（{(enabled ? "有効" : "無効にされていたので外しただけ")}）。");
            return true;
        }
        catch (Exception ex)
        {
            log?.Error($"旧名のスタートアップの登録を引き継げません: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// StartupApproved の値の先頭の1バイトが奇数（0x03 など）なら、利用者が無効にしている。
    /// 有効なら 0x02 などの偶数で、値そのものがないときも有効として扱う（Windows と同じ）。
    /// </summary>
    public static bool IsDisabledByUser(byte[]? approved) => approved is { Length: > 0 } && (approved[0] & 1) == 1;

    /// <summary>Run の1行から実行ファイルのパスを取り出す（引用符で囲んであってもなくてもよい）。</summary>
    public static string ExecutableOf(string command)
    {
        var text = command.Trim();

        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            return close > 1 ? text[1..close] : text.Trim('"');
        }

        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }
}
