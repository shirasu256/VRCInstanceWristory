namespace VRCInstanceWristory.Core;

/// <summary>ログを読めなかった理由（→実装メモ5.85）。状態の段の値を分けるのに使う。</summary>
public enum LogReadFailure
{
    None,

    /// <summary>理由の分からない失敗。</summary>
    Other,

    /// <summary>読む権限がない（<see cref="UnauthorizedAccessException"/>）。</summary>
    AccessDenied,

    /// <summary>ほかのプロセスが共有を許さずに開いている（共有違反・ロック違反）。</summary>
    SharingViolation,

    /// <summary>ドライブが見つからない・準備ができていない（外付けのドライブを外した、など）。</summary>
    DriveMissing,
}

public static class LogReadFailures
{
    /// <summary>ログを読めなかった例外から、理由を分ける（→実装メモ5.85）。</summary>
    public static LogReadFailure Classify(Exception ex)
    {
        const int SharingViolation = unchecked((int)0x80070020);
        const int LockViolation = unchecked((int)0x80070021);
        const int NotReady = unchecked((int)0x80070015);
        const int DeviceMissing = unchecked((int)0x80070037);

        return ex switch
        {
            UnauthorizedAccessException => LogReadFailure.AccessDenied,
            DriveNotFoundException or DirectoryNotFoundException => LogReadFailure.DriveMissing,
            IOException { HResult: SharingViolation or LockViolation } => LogReadFailure.SharingViolation,
            IOException { HResult: NotReady or DeviceMissing } => LogReadFailure.DriveMissing,
            _ => LogReadFailure.Other,
        };
    }
}
