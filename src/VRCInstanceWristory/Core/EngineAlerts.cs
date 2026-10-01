namespace VRCInstanceWristory.Core;

/// <summary>
/// 状態の段（ウィンドウの左下）に出すための、エンジンが見つけた問題（2026-09-28のユーザー指定→実装メモ5.85）。
/// 表示の条件（<see cref="EngineSnapshot.ContentReady"/>）には使わない。時間の長さで出し分けるものは、
/// 始まった時刻を渡し、長さは受け取る側（主ループ）が今の時刻と比べて決める。
/// </summary>
public sealed record EngineAlerts
{
    public static readonly EngineAlerts None = new();

    /// <summary>動いている VRChat のプロセスの数。2つ以上なら、どちらのログを追うか決めずに止める。</summary>
    public int ClientCount { get; init; }

    /// <summary>動いている VRChat の起動時刻（1つだけ動いているとき）。「ここへ戻る」で起動し直したかを見分けるのに使う。</summary>
    public DateTime? ClientStartUtc { get; init; }

    /// <summary>プロセスの一覧を取れない。</summary>
    public bool ProcessListFailing { get; init; }

    /// <summary>VRChat のプロセスは見つかったが、情報（起動時刻）を読めないことが続いている。</summary>
    public bool ProcessInfoUnreadable { get; init; }

    /// <summary>
    /// VRChat が終了の記録を残さずに終わったのを見た時刻（このアプリが動いている間に、プロセスの終了で気づいたもの）。
    /// 起動時の全走査で見つけた過去のクラッシュは入れない。
    /// </summary>
    public DateTime? CrashDetectedUtc { get; init; }

    public LogFolderState LogFolder { get; init; }

    /// <summary><see cref="LogHealth.ReadError"/> のときの理由。</summary>
    public LogReadFailure ReadFailure { get; init; }

    /// <summary>いまの <see cref="EngineSnapshot.Health"/> になった時刻。</summary>
    public DateTime HealthSinceUtc { get; init; }

    /// <summary>実行中の VRChat のログに、最後に行が増えた時刻（増えるのを待ち始めた時刻を含む）。</summary>
    public DateTime? LastAppendUtc { get; init; }

    /// <summary>VRChat の更新でログの形式が変わり、インスタンスの行を読み取れていない疑いがある。</summary>
    public bool FormatSuspect { get; init; }

    /// <summary>60分以内の起動し直しが続き、さかのぼって読む上限で古いログを読まなかった。</summary>
    public bool HistoryTruncated { get; init; }

    /// <summary>起動時にチェックポイント（内部履歴）を読めず、ログから組み立て直した時刻。</summary>
    public DateTime? CheckpointUnreadableUtc { get; init; }

    /// <summary>チェックポイント・目印を保存できていない。</summary>
    public bool SaveFailing { get; init; }

    /// <summary>
    /// いまの時刻から、読んだばかりの行の時刻を引いたもの（実行中の VRChat のログを追っている間に、最後に読んだ行）。
    /// 起動中に PC の時計や時間帯を変えると、VRChat の書く時刻とずれる。
    /// </summary>
    public TimeSpan? ClockSkew { get; init; }

    /// <summary>実行中の VRChat が使っている VR の方式（ログの <c>StartVRSDK:</c>）。デスクトップモードや不明なら null。</summary>
    public string? VrSdk { get; init; }
}
