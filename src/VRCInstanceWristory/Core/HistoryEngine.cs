using System.Globalization;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Logging;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Core;

/// <summary>
/// ログ監視・訪問確定・履歴・回数をまとめる中核。OpenVRに依存しないので単体で検証できる。
/// 状態更新はこのクラスの呼び出し（単一スレッド）に集約し、描画・プロセス通知と競合させない。
/// </summary>
public sealed partial class HistoryEngine : IDisposable
{
    private readonly EngineOptions _options;
    private readonly IClock _clock;
    private readonly LogTimeConverter _time;
    private readonly IProcessProvider _processes;
    private readonly IDiagnostics _log;
    private readonly CheckpointStore? _checkpoint;

    private readonly LogSourceRegistry _registry = new();
    private readonly VisitCounter _counter = new();
    private readonly HistoryStore _history = new();
    private readonly Dictionary<string, OpenSource> _sources = new(StringComparer.Ordinal);

    // 目印（→実装メモ5.32・5.54）。インスタンスごと（鍵は locationKey）に付け、アプリを終了しても残す。
    // 付けてから次のログのリセット（消去の区切りが進んだとき）で消す（→ApplyRetention）。
    private readonly MarkStore _marks = new();
    private readonly MarkFile? _markFile;

    // 訪問履歴の保存（→実装メモ5.108）。自動リセットが無効のときは、起動時にここから行を戻す。
    private readonly HistoryFile? _historyFile;
    private long _historySavedVersion = -1;
    private DateTime _historySavedUtc = DateTime.MinValue;
    private bool _historySaveFailed;

    // 戻した訪問履歴を保存した時刻。戻した行の退出がログから分からないときの終わりの見積もりに使う。
    private DateTime? _restoredSavedAtUtc;

    private ClientProcessInfo? _client;
    private bool _ambiguousClient;
    private string? _activeSourceId;
    private LogHealth _health = LogHealth.Initializing;
    private string? _diagnostic;
    private bool _checkpointDirty;
    private bool _checkpointHealthy = true;
    private long _generation;

    // 状態の段に出すための記録（→実装メモ5.85）。表示の条件には使わない。
    private DateTime _healthSinceUtc;
    private LogReadFailure _readFailure;
    private LogFolderState _logFolder;

    /// <summary>直近に記録した、行の処理で起きた例外（同じものを読み直しのたびに記録しないため）。</summary>
    private string? _lastProcessingError;
    private DateTime? _crashDetectedUtc;
    private DateTime? _lastAppendUtc;
    private DateTime _lastUpdateUtc = DateTime.MinValue;
    private TimeSpan? _clockSkew;
    private bool _historyTruncated;
    private DateTime? _checkpointUnreadableUtc;
    private bool _marksSaveFailed;

    /// <summary>主ループがこれより長く止まっていたら、スリープなどで止まっていたとみなす（ログが増えない時間に数えない）。</summary>
    private static readonly TimeSpan SuspendGap = TimeSpan.FromSeconds(60);

    /// <summary>いま記録している種類（→<see cref="EngineOptions.TargetTypes"/>）。</summary>
    private HashSet<AccessType> _targets;

    private bool _showDuringLoading;

    private DateTime _lastTail = DateTime.MinValue;
    private DateTime _lastScan = DateTime.MinValue;
    private DateTime _lastMaintenance = DateTime.MinValue;

    public HistoryEngine(
        EngineOptions options,
        IClock clock,
        LogTimeConverter time,
        IProcessProvider processes,
        IDiagnostics? log = null,
        CheckpointStore? checkpoint = null,
        MarkFile? markFile = null,
        HistoryFile? historyFile = null)
    {
        _options = options;
        _clock = clock;
        _time = time;
        _processes = processes;
        _log = log ?? NullDiagnostics.Instance;
        _checkpoint = checkpoint;
        _markFile = markFile;
        _historyFile = historyFile;
        _retention = options.Retention > TimeSpan.Zero ? options.Retention : HistoryStore.DefaultRetention;
        _autoReset = options.AutoReset;
        _targets = [.. options.TargetTypes];
        _showDuringLoading = options.ShowDuringLoadingScreen;
        _stopInTarget = options.StopCountdownInTarget;
    }

    /// <summary>いま記録している種類。</summary>
    public IReadOnlySet<AccessType> TargetTypes => _targets;

    /// <summary>回数の台帳（検証用。表示には各行の <see cref="VisitRecord.VisitOrdinal"/> を使う）。</summary>
    public VisitCounter Counter => _counter;

    public LogHealth Health => _health;

    /// <summary>履歴へ新しく追加された対象訪問。検証での集計に使う。</summary>
    public event Action<VisitRecord>? VisitAdded;

    /// <summary>起動時の復元（仕様4.2節）。完了するまで表示しない。</summary>
    public void Initialize()
    {
        _health = LogHealth.Initializing;
        _healthSinceUtc = _clock.UtcNow;

        LoadCheckpoint();
        LoadMarks();
        LoadHistory();

        _processes.Poll();
        DrainProcessEvents();

        ScanDirectory(initial: true);
        MarkDirty();
        SaveCheckpointIfNeeded(force: true);
        SaveHistoryIfNeeded(force: true);
    }

    /// <summary>
    /// VR側の操作（B / Y ボタン）でパネルを閉じる。
    /// ログ側の「開く」条件（ワールドタブ）はそのままで、閉じる指示だけを受け取る。
    /// </summary>
    public void ClosePanel()
    {
        if (_activeSourceId is null || !_sources.TryGetValue(_activeSourceId, out var source))
            return;

        if (!source.Tracker.MenuPageOpen)
            return;

        source.Tracker.CloseMenuPage();
        MarkDirty();
    }

    /// <summary>
    /// 手首の角度でパネルが隠れたことをVR側から伝える（2026-09-21のユーザー指定）。
    ///
    /// インスタンスを移ってもパネルは出したままにし、移動先への入室が済んだあと
    /// 最初に角度で隠れた時点で閉じる。それ以外のときは何もしないので、
    /// VR側は隠れている間ずっと呼んでよい。
    /// </summary>
    public void CloseByViewAngle()
    {
        if (_activeSourceId is null || !_sources.TryGetValue(_activeSourceId, out var source))
            return;

        if (!source.Tracker.AwaitingViewAngleClose)
            return;

        source.Tracker.CloseMenuPage();
        _log.Info("ロード画面のあと手首の角度で隠れたため、パネルを閉じました。");
        MarkDirty();
    }

    /// <summary>
    /// VR側の操作（行のポップアップ）で目印を付け外しする（2026-09-22のユーザー指定→実装メモ5.32）。
    ///
    /// 目印は1回の訪問ではなくインスタンスに付くので、同じインスタンス
    /// （<see cref="VisitRecord.LocationKey"/> が同じ行）はすべて同じ印になる。
    /// 既に付いている印をもう一度選んだときは外す。付け外しするたびに保存する（→実装メモ5.54）。
    /// </summary>
    public void SetMark(string eventId, InstanceMark mark)
    {
        if (_history.Find(eventId) is not { } record)
            return;

        if (!_marks.Toggle(record.LocationKey, mark, _clock.UtcNow))
            return;

        var applied = _marks.Get(record.LocationKey);

        _log.Info(applied == InstanceMark.None
            ? $"インスタンス {record.InstanceId} の目印を外しました。"
            : $"インスタンス {record.InstanceId} に目印（{applied}）を付けました。");

        SaveMarks();

        // 行の見た目が変わるので描き直す。
        MarkDirty();
    }

    /// <summary>行（eventId）から、その訪問の記録を引く。「ここへ戻る」で location を得るのに使う。</summary>
    public VisitRecord? FindVisit(string eventId) => _history.Find(eventId);

    /// <summary>
    /// 記録する種類を変える（2026-09-26のユーザー指定でデスクトップのウィンドウから選べるようにした→実装メモ5.39）。
    ///
    /// どの訪問が行になるかだけでなく、「対象を離れた時刻」（＝消すまでの数え始め）と
    /// 「対象外のインスタンスへ移動」の帯もこの区別で決まる。行を足し引きするだけでは
    /// これらが合わないので、ログを先頭から読み直して一覧を作り直す（起動時の復元と同じ経路）。
    ///
    /// 回数は読み直しても二重に数えない（適用位置と保存済みのイベントは残したまま）。
    /// 記録しない種類の訪問も回数だけは数えているので（→実装メモ5.58）、これまで記録していなかった種類の
    /// 過去の訪問にも、そのときの回数が付く。
    /// 消えた行の区切り・「延長」を押した時刻・目印は読み直しの前後で引き継ぐ。
    ///
    /// 残す種類の行は、読み直す前に保存から戻した行として残しておく（→実装メモ5.108）。ログが消えた訪問は読み直しでは戻らないため。
    /// ログを読めた訪問は、読み直した行へ置き換わる。
    /// </summary>
    public void SetTargetTypes(IReadOnlySet<AccessType> types)
    {
        if (_targets.SetEquals(types))
            return;

        _targets = [.. types];

        foreach (var source in _sources.Values)
            CloseReader(source);

        var kept = _history.Records.Where(r => _targets.Contains(r.AccessType)).ToList();

        // 読み直すソースは新しい種類で作り直す（現在地・候補・退出時刻も種類の区別に依存する）。
        _sources.Clear();
        _history.Clear();
        _history.Restore(kept);

        ScanDirectory(initial: true);
        SaveCheckpointIfNeeded(force: true);
        SaveHistoryIfNeeded(force: true);

        _log.Info($"記録する種類を変えたため、ログを読み直しました: {string.Join(" / ", TargetAccessTypes.Selectable.Where(_targets.Contains).Select(t => t.DisplayName()))}");
        MarkDirty();
    }

    /// <summary>
    /// ロード画面の間もパネルを出すかを変える（→実装メモ5.62）。読み直しは要らない。
    /// ロード画面の途中で変えたら、その場で出す・隠すにも反映する。
    /// </summary>
    public void SetShowDuringLoadingScreen(bool show)
    {
        if (_showDuringLoading == show)
            return;

        _showDuringLoading = show;

        foreach (var source in _sources.Values)
            source.Tracker.ShowDuringLoadingScreen = show;

        _log.Info($"ロード画面の間のパネル表示を{(show ? "オン" : "オフ")}にしました。");
        MarkDirty();
    }

    /// <summary>主ループから毎フレーム呼ぶ。内部で間隔を管理する。</summary>
    public void Update()
    {
        var now = _clock.UtcNow;

        // スリープの間は VRChat もログを書かないので、ログが増えない時間には数えない（→実装メモ5.85）。
        if (_lastUpdateUtc != DateTime.MinValue && now - _lastUpdateUtc > SuspendGap && _lastAppendUtc is not null)
            _lastAppendUtc = now;

        _lastUpdateUtc = now;

        DrainProcessEvents();

        if (now - _lastScan >= _options.DirectoryScanInterval)
        {
            _lastScan = now;
            ScanDirectory(initial: false);
        }

        if (now - _lastTail >= _options.TailInterval)
        {
            _lastTail = now;
            TailActiveSource();
        }

        if (now - _lastMaintenance >= _options.MaintenanceInterval)
        {
            _lastMaintenance = now;
            Maintenance();
        }
    }

    public EngineSnapshot Snapshot()
    {
        var now = _clock.UtcNow;
        var active = _activeSourceId is null ? null : _sources.GetValueOrDefault(_activeSourceId);
        var presence = active?.Tracker.State ?? PresenceState.Unknown;

        // 期限ちょうどで消えるように、1秒ごとの消去を待たずにここでも区切りを見る。
        var cutoff = EffectiveCutoffUtc;
        if (_retentionDeadlineUtc is { } deadline && deadline <= now && deadline > cutoff)
            cutoff = deadline;

        return new EngineSnapshot
        {
            ClientRunning = _client is not null,
            Presence = presence,
            Health = _health,
            History = _history.GetVisible(now, cutoff),
            Marks = _marks.All,
            CurrentEventId = presence == PresenceState.InTarget ? active?.Tracker.CurrentEventId : null,
            MenuPageOpen = active?.Tracker.MenuPageOpen ?? false,
            AwaitingViewAngleClose = active?.Tracker.AwaitingViewAngleClose ?? false,
            InLoadingScreen = active?.Tracker.InLoadingScreen ?? false,
            BaselineKnown = _counter.BaselineKnown,
            CheckpointHealthy = _checkpointHealthy,
            Alerts = new EngineAlerts
            {
                ClientCount = _processes.Running.Count,
                ClientStartUtc = _client?.StartUtc,
                ProcessListFailing = _processes.ListFailing,
                ProcessInfoUnreadable = _processes.InfoUnreadable,
                CrashDetectedUtc = _crashDetectedUtc,
                LogFolder = _logFolder,
                ReadFailure = _health == LogHealth.ReadError ? _readFailure : LogReadFailure.None,
                HealthSinceUtc = _healthSinceUtc,
                LastAppendUtc = active is not null && _client is not null ? _lastAppendUtc : null,
                FormatSuspect = active?.Tracker.FormatSuspect ?? false,
                HistoryTruncated = _historyTruncated,
                CheckpointUnreadableUtc = _checkpointUnreadableUtc,
                SaveFailing = !_checkpointHealthy || _marksSaveFailed || _historySaveFailed,
                ClockSkew = active is not null && _client is not null ? _clockSkew : null,
                VrSdk = _client is not null ? active?.VrSdk : null,
            },
            Diagnostic = _diagnostic,
            Generation = _generation,
            RetentionDeadlineUtc = _retentionDeadlineUtc,
            RetentionPausedRemaining = _retentionDeadlineUtc is null ? null : _pausedRemaining,
            Retention = _retention,
            AutoReset = _autoReset,
        };
    }

    private void Maintenance()
    {
        var now = _clock.UtcNow;

        // 終了通知と併用して、存否を1秒ごとに再確認する（仕様7節）。
        _processes.Poll();
        DrainProcessEvents();

        ApplyRetention(now);
        SaveCheckpointIfNeeded(force: false);
        SaveHistoryIfNeeded(force: false);
    }

    private void SetHealth(LogHealth health, string? diagnostic)
    {
        if (_health == health && _diagnostic == diagnostic)
            return;

        if (_health != health)
            _healthSinceUtc = _clock.UtcNow;

        _health = health;
        _diagnostic = diagnostic;

        if (diagnostic is not null)
            _log.Warn(diagnostic);

        MarkDirty();
    }

    private void MarkDirty() => _generation++;

    /// <summary>記録に書く時刻（ローカル・<c>HH:mm:ss</c>）。区切りの文字が地域の設定で変わらないようにする。</summary>
    private string LocalClock(DateTime utc) => _time.ToLocal(utc).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        SaveCheckpointIfNeeded(force: _checkpointDirty);
        SaveHistoryIfNeeded(force: true);

        foreach (var source in _sources.Values)
            CloseReader(source);

        _sources.Clear();
    }
}
