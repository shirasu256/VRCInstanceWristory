using System.Runtime.InteropServices;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// プロセスが管理者として（昇格して）動いているかを調べる（→実装メモ5.85）。
///
/// SteamVR を管理者として起動し、このアプリを普通に起動すると、SteamVR との通信に失敗する。
/// 通信の失敗（<c>IPC_*</c>）のときにだけ SteamVR のサーバーを調べ、「権限レベル不一致」と出し分ける。
///
/// 管理者でないプロセスからは、管理者のプロセスのトークンを開けない（アクセス拒否）。
/// 開けなかったこと自体を「管理者として動いている」の手がかりにする。
/// </summary>
public static partial class ProcessElevation
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private const int ErrorAccessDenied = 5;

    /// <summary>このプロセスが管理者として動いているか。</summary>
    public static bool CurrentIsElevated() => IsElevated(Environment.ProcessId) == true;

    /// <summary>
    /// そのプロセスが管理者として動いているか。調べられなければ null。
    /// このプロセスが管理者でなく、相手のトークンを開けずにアクセス拒否になったときは、管理者として動いているとみなす。
    /// </summary>
    public static bool? IsElevated(int pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);

        if (process == nint.Zero)
            return null;

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
                return Marshal.GetLastPInvokeError() == ErrorAccessDenied && pid != Environment.ProcessId ? true : null;

            try
            {
                return GetTokenInformation(token, TokenElevationClass, out var elevated, sizeof(uint), out _)
                    ? elevated != 0
                    : null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int informationClass, out uint information, int length, out int returned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
