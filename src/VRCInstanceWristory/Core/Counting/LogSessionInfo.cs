using System.Globalization;

namespace VRCInstanceWristory.Core.Counting;

/// <summary>
/// 1つのVRChat起動（＝1つのログセッション）についての、時刻と出典の記録。
/// ログファイルが削除された後も、セッションの終わり（保存から戻した行の退出の見積もり→実装メモ5.108）に使うためチェックポイントへ残す。
/// </summary>
public sealed class LogSessionInfo
{
    public required string SourceSessionId { get; init; }

    /// <summary>ファイル名から得たログセッション開始時刻（出典 = Log）。</summary>
    public required DateTime SessionStartUtc { get; init; }

    /// <summary>対応づいたプロセスの開始時刻（出典 = Process）。</summary>
    public DateTime? ProcessStartUtc { get; set; }

    /// <summary>HandleApplicationQuit の時刻。終了処理開始であり、OS上の終了時刻ではない（出典 = Log）。</summary>
    public DateTime? QuitAtUtc { get; set; }

    /// <summary>観測できたプロセスの実際の終了時刻（出典 = Process）。</summary>
    public DateTime? ProcessExitUtc { get; set; }

    /// <summary>
    /// ログの最後の行の時刻（出典 = Log）。VRChatはこの時刻まで動いていたので、終わりの下限になる。
    /// 終わりの時刻そのものとしては使わない（仕様3.3節→実装メモ5.60）。
    /// </summary>
    public DateTime? LastLogUtc { get; set; }



    public DateTime StartUtc => ProcessStartUtc ?? SessionStartUtc;

    public TimeSource StartSource => ProcessStartUtc is not null ? TimeSource.Process : TimeSource.Log;

    public DateTime? ExitUtc => ProcessExitUtc ?? QuitAtUtc;

    public TimeSource ExitSource =>
        ProcessExitUtc is not null ? TimeSource.Process :
        QuitAtUtc is not null ? TimeSource.Log :
        TimeSource.Unknown;

    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{SourceSessionId} start={StartUtc:u}({StartSource}) exit={(ExitUtc is { } exit ? exit.ToString("u", CultureInfo.InvariantCulture) : "-")}({ExitSource})");
}
