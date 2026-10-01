namespace VRCInstanceWristory.Core.Counting;

/// <summary>counter-state.json の内容（仕様3.4節）。原文ログや対象外の訪問一覧は含めない。</summary>
public sealed class CheckpointDto
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string? EpochId { get; set; }

    /// <summary>false なら、この期間に付ける回数は不明として扱う。</summary>
    public bool BaselineKnown { get; set; }

    public AnchorDto? Anchor { get; set; }

    /// <summary>locationKey → カウント期間内の訪問回数。</summary>
    public Dictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    public List<SourceDto> Sources { get; set; } = [];

    /// <summary>直近60分の各eventIdに付けた回数と時刻。</summary>
    public List<EventDto> RecentEvents { get; set; } = [];

    /// <summary>
    /// 「延長」を押した時刻（古い順・UTC）。再起動しても延長を失わないために残す（→実装メモ5.45）。
    /// これより前の版のファイルにはないので、読めなければ空として扱う（形式の番号は上げない）。
    /// </summary>
    public List<DateTime> RetentionResets { get; set; } = [];

    /// <summary>
    /// 履歴の消去の区切り（UTC）。これより前に入った行は消えたものとして、再起動しても戻さない（→実装メモ5.45）。
    /// </summary>
    public DateTime? HistoryCutoffUtc { get; set; }

    /// <summary>
    /// 履歴の自動リセットを最後にオンへ戻した時刻（UTC→実装メモ5.71）。これより前の退出・延長からは期限を数えない。
    /// 前の版のファイルにはないので、読めなければ null（すべての退出から数える＝従来どおり）。
    /// </summary>
    public DateTime? AutoResetFromUtc { get; set; }

    /// <summary>
    /// 行の上限（500件・30日→実装メモ5.108）で消した区切り（UTC）。<see cref="HistoryCutoffUtc"/> とは別に持つ
    /// （こちらはリセットではないので、目印を消す・「直前のリセットを戻す」の対象にしない）。
    /// 前の版のファイルにはないので、読めなければ null。
    /// </summary>
    public DateTime? HistoryLimitCutoffUtc { get; set; }

    /// <summary>保存した時刻（診断用）。</summary>
    public DateTime SavedAtUtc { get; set; }

    public sealed class AnchorDto
    {
        public string SourceSessionId { get; set; } = string.Empty;
        public DateTime StartUtc { get; set; }
        public string Reason { get; set; } = string.Empty;

        public static AnchorDto From(EpochAnchor anchor) => new()
        {
            SourceSessionId = anchor.SourceSessionId,
            StartUtc = anchor.StartUtc,
            Reason = anchor.Reason,
        };

        public EpochAnchor ToAnchor() => new(SourceSessionId, StartUtc, Reason);
    }

    public sealed class SourceDto
    {
        public string SourceSessionId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? PrefixHash { get; set; }
        public int PrefixLength { get; set; }
        public DateTime? CreatedUtc { get; set; }
        public long AppliedOffset { get; set; }
        public DateTime SessionStartUtc { get; set; }
        public DateTime? ProcessStartUtc { get; set; }
        public DateTime? QuitAtUtc { get; set; }
        public DateTime? ProcessExitUtc { get; set; }

        /// <summary>
        /// ログの最後の行の時刻（→実装メモ5.60）。ログが消えた後も終わりの下限を使えるように残す。
        /// これより前の版のファイルにはないので、読めなければ null として扱う（形式の番号は上げない）。
        /// </summary>
        public DateTime? LastLogUtc { get; set; }

        public bool FileMissing { get; set; }

        public static SourceDto From(LogSourceEntry e) => new()
        {
            SourceSessionId = e.SourceSessionId,
            FileName = e.FileName,
            PrefixHash = e.PrefixHash,
            PrefixLength = e.PrefixLength,
            CreatedUtc = e.CreatedUtc,
            AppliedOffset = e.AppliedOffset,
            SessionStartUtc = e.Session.SessionStartUtc,
            ProcessStartUtc = e.Session.ProcessStartUtc,
            QuitAtUtc = e.Session.QuitAtUtc,
            ProcessExitUtc = e.Session.ProcessExitUtc,
            LastLogUtc = e.Session.LastLogUtc,
            FileMissing = e.FileMissing,
        };

        public LogSourceEntry ToEntry() => new()
        {
            SourceSessionId = SourceSessionId,
            FileName = FileName,
            PrefixHash = PrefixHash,
            PrefixLength = PrefixLength,
            CreatedUtc = CreatedUtc,
            AppliedOffset = AppliedOffset,
            FileMissing = FileMissing,
            Session = new LogSessionInfo
            {
                SourceSessionId = SourceSessionId,
                SessionStartUtc = SessionStartUtc,
                ProcessStartUtc = ProcessStartUtc,
                QuitAtUtc = QuitAtUtc,
                ProcessExitUtc = ProcessExitUtc,
                LastLogUtc = LastLogUtc,
            },
        };
    }

    public sealed class EventDto
    {
        public string EventId { get; set; } = string.Empty;
        public string LocationKey { get; set; } = string.Empty;
        public DateTime VisitedAtUtc { get; set; }
        public int? Ordinal { get; set; }
        public string EpochId { get; set; } = string.Empty;

        public static EventDto From(StoredEvent e) => new()
        {
            EventId = e.EventId,
            LocationKey = e.LocationKey,
            VisitedAtUtc = e.VisitedAtUtc,
            Ordinal = e.Ordinal,
            EpochId = e.EpochId,
        };

        public StoredEvent ToEvent() => new(EventId, LocationKey, VisitedAtUtc, Ordinal, EpochId);
    }
}
