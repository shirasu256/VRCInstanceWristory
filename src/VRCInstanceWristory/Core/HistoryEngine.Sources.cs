using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Logging;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Core;

// ログのソース（フォルダーの走査・ファイルとの対応づけ・起動時の復元・追記の追従）と、VRChat の起動・終了・クラッシュの扱い。
public sealed partial class HistoryEngine
{
    /// <summary>さかのぼって読む連続セッションの上限。切れ目のない再起動が続いた場合の歯止め。</summary>
    private const int MaxRetentionChainSessions = 24;

    private List<PendingCount>? _deferredCounts;

    // 走査のたびに古いログの先頭を読み直さないよう、大きさと更新時刻が同じ間はハッシュを覚えておく。
    private readonly PrefixHashCache _prefixHashes = new();

    // ------------------------------------------------------------------ プロセス

    private void DrainProcessEvents()
    {
        while (_processes.TryDequeue(out var ev))
        {
            switch (ev.Kind)
            {
                case ProcessEventKind.Started:
                    OnClientStarted(ev);
                    break;

                case ProcessEventKind.Exited:
                    OnClientExited(ev);
                    break;
            }
        }

        if (_processes.Running.Count > 1)
        {
            _ambiguousClient = true;
            SetHealth(LogHealth.AmbiguousClient, $"VRChatプロセスが{_processes.Running.Count}個あります。自動選択せず非表示にします。");
            _client = null;
            _activeSourceId = null;
            return;
        }

        if (_ambiguousClient)
        {
            _ambiguousClient = false;
            SetHealth(LogHealth.Ok, null);
        }

        _client = _processes.Running.Count == 1 ? _processes.Running[0] : null;
    }

    private void OnClientStarted(in ProcessEvent ev)
    {
        _log.Info($"VRChat 起動 pid={ev.Process.Pid} start={LocalClock(ev.Process.StartUtc)}（出典 {ev.Source}）");
        _client = ev.Process;
        _lastAppendUtc = _clock.UtcNow;
        _clockSkew = null;

        // 対応するログセッションは走査で決める。現在地は新しいセッションから作り直す。
        _activeSourceId = null;
        MarkDirty();
        ScanDirectory(initial: false);
    }

    private void OnClientExited(in ProcessEvent ev)
    {
        _log.Info($"VRChat 終了 pid={ev.Process.Pid} at={LocalClock(ev.AtUtc)}（出典 {ev.Source}）");

        // 表示は直ちに止めるが、未読の過去ログは捨てない（仕様7節）。
        _client = null;
        MarkDirty();

        if (_activeSourceId is not null && _sources.TryGetValue(_activeSourceId, out var source))
        {
            source.Entry.Session.ProcessExitUtc = ev.Source == TimeSource.Process ? ev.AtUtc : source.Entry.Session.ProcessExitUtc;

            // 終了までに書かれた行をすべて読んでから終わりを確定する。
            // 正常終了のログはプロセスが消える直前に書かれるので、ここで読まないと
            // 正常な終了までクラッシュに見えてしまう。
            ReadSource(source);
            FinalizeEndedSession(source, ev.AtUtc);
            CloseReader(source);
        }

        _checkpointDirty = true;
        SaveCheckpointIfNeeded(force: true);
    }

    // ------------------------------------------------------------------ 走査

    private void ScanDirectory(bool initial)
    {
        var files = Infrastructure.LogDirectory.EnumerateLogFiles(_options.LogDirectory);

        if (_options.OnlyFileName is { } only)
            files = files.Where(f => string.Equals(Path.GetFileName(f), only, StringComparison.OrdinalIgnoreCase)).ToList();

        if (files.Count == 0 && initial)
            _log.Warn($"ログファイルが見つかりません: {_options.LogDirectory}");

        _logFolder = files.Count > 0
            ? LogFolderState.Ok
            : Directory.Exists(_options.LogDirectory) ? LogFolderState.Empty : LogFolderState.Missing;

        var known = new List<(string Path, LogSourceEntry Entry)>();
        var replaced = false;

        foreach (var path in files)
        {
            var entry = MatchOrCreate(path, ref replaced);
            if (entry is not null)
                known.Add((path, entry));
        }

        _prefixHashes.Retain(files);

        var present = known.Select(k => k.Entry.SourceSessionId).ToHashSet(StringComparer.Ordinal);

        foreach (var e in _registry.Entries)
            e.FileMissing = !present.Contains(e.SourceSessionId);

        // ファイルがなく、いまのカウント期間より前の記録は捨てる（台帳とチェックポイントが起動のたびに伸び続けないように）。
        // 捨てるのは起動のときの走査で、フォルダーを読めたときだけ。動いている間にフォルダーが一時的に読めなくなった
        // （すべてのファイルが「ない」に見える）だけで捨てると、ファイルが戻ったときに同じセッションを新しいIDで読み直し、行が二重になる。
        if (initial
            && _logFolder == LogFolderState.Ok
            && _counter.Anchor is { } anchor
            && _registry.PruneMissingBefore(anchor.StartUtc, anchor.SourceSessionId) is > 0 and var pruned)
        {
            _checkpointDirty = true;
            _log.Info($"カウント期間より前の、ファイルのないログの記録を{pruned}件捨てました。");
        }

        var previousActive = _activeSourceId;
        _activeSourceId = SelectActiveSource(known);

        if (previousActive != _activeSourceId)
        {
            _lastAppendUtc = _clock.UtcNow;
            _clockSkew = null;
        }

        // 差し替えられたファイルは、新しいソースとして読み直す。
        if (initial || replaced || previousActive != _activeSourceId)
        {
            RestoreFromLogs(known);
        }
    }

    /// <summary>差し替えられたファイルの旧ソースを閉じ、そこから作った履歴行を捨てる。</summary>
    private void DiscardSource(LogSourceEntry entry)
    {
        entry.FileMissing = true;

        if (_sources.TryGetValue(entry.SourceSessionId, out var source))
        {
            CloseReader(source);
            _sources.Remove(entry.SourceSessionId);
        }

        if (_history.RemoveBySource(entry.SourceSessionId) > 0)
            MarkDirty();

        if (string.Equals(_activeSourceId, entry.SourceSessionId, StringComparison.Ordinal))
            _activeSourceId = null;
    }

    /// <summary>
    /// 走査で見つけたファイルを既知のソースへ対応づける。知らないファイルなら新しいソースとして登録する。
    /// 同じ名前で先頭が変わっていれば（差し替え）、旧ソースを捨ててから新しいソースを登録し、<paramref name="replaced"/> を立てる。
    /// </summary>
    private LogSourceEntry? MatchOrCreate(string path, ref bool replaced)
    {
        var fileName = Path.GetFileName(path);

        if (!Infrastructure.LogDirectory.TryParseSessionStart(fileName, out var sessionStartLocal))
            return null;

        if (!_time.TryToUtc(sessionStartLocal, out var sessionStartUtc))
            return null;

        if (!_prefixHashes.TryGet(path, out var hash, out var hashLength))
        {
            // 一時的に開けないだけのことがある。既知のファイルなら対応づけを維持し、
            // 現在地を見失ってパネルを消さない。
            return _registry.FindByFileName(fileName);
        }

        var created = Infrastructure.LogDirectory.TryGetCreationTimeUtc(path);
        var match = _registry.Match(fileName, created, hash, hashLength);

        switch (match)
        {
            case { Kind: LogSourceMatchKind.Same, Entry: { } existing }:
                existing.PrefixHash = hash;
                existing.PrefixLength = hashLength;
                existing.CreatedUtc ??= created;
                existing.FileMissing = false;
                return existing;

            case { Kind: LogSourceMatchKind.Replaced, Entry: { } old }:
                // 差し替え。同じIDを再利用せず、旧ソース由来の行も残さない。
                _log.Warn($"ログの先頭が変化しました: {fileName}。新しいソースとして扱います。");
                DiscardSource(old);
                replaced = true;
                break;
        }

        var id = _registry.AllocateId(_options.SourceNamespace);
        var entry = new LogSourceEntry
        {
            SourceSessionId = id,
            FileName = fileName,
            PrefixHash = hash,
            PrefixLength = hashLength,
            CreatedUtc = created,
            AppliedOffset = 0,
            Session = new LogSessionInfo
            {
                SourceSessionId = id,
                SessionStartUtc = sessionStartUtc,
            },
        };

        _registry.Add(entry);
        _log.Info($"新しいログソース {id}: {fileName}");
        return entry;
    }

    /// <summary>実行中プロセスに対応するログセッションを選ぶ（仕様7節）。更新日時の新しさだけでは選ばない。</summary>
    private string? SelectActiveSource(List<(string Path, LogSourceEntry Entry)> known)
    {
        if (_client is not { } client)
        {
            _diagnostic = null;
            return null;
        }

        var candidates = known
            .Where(k =>
            {
                var start = k.Entry.Session.SessionStartUtc;
                return start >= client.StartUtc - _options.ProcessMatchBefore
                    && start <= client.StartUtc + _options.ProcessMatchAfter;
            })
            .OrderByDescending(k => k.Entry.Session.SessionStartUtc)
            .ToList();

        if (candidates.Count == 0)
        {
            SetHealth(LogHealth.NoLogMatch, "実行中のVRChatに対応するログセッションをまだ特定できません。");
            return null;
        }

        var chosen = candidates[0].Entry;
        chosen.Session.ProcessStartUtc = client.StartUtc;
        return chosen.SourceSessionId;
    }

    // ------------------------------------------------------------------ 復元・追従

    private void RestoreFromLogs(List<(string Path, LogSourceEntry Entry)> known)
    {
        var now = _clock.UtcNow;
        var sessions = _registry.OrderedSessions();
        var order = sessions
            .Select((s, i) => (s.SourceSessionId, Index: i))
            .ToDictionary(x => x.SourceSessionId, x => x.Index, StringComparer.Ordinal);

        // 第1段: ソースを順に再生して、現在地・入室候補・履歴行と、各セッションの終了時刻を組み直す。
        // この時点ではカウンターへ適用しない（起点の判定に終了時刻が要るため）。
        _deferredCounts = [];

        var byId = known.ToDictionary(k => k.Entry.SourceSessionId, k => k, StringComparer.Ordinal);

        bool ReadSession(string sourceSessionId)
        {
            if (!byId.TryGetValue(sourceSessionId, out var item))
                return false;

            var index = order.GetValueOrDefault(sourceSessionId, int.MaxValue);
            var source = OpenOrGetSource(item.Path, item.Entry, index);

            if (source is null || source.Replayed)
                return false;

            ReadSource(source);
            source.Replayed = true;

            if (!string.Equals(sourceSessionId, _activeSourceId, StringComparison.Ordinal))
                CloseReader(source);

            return true;
        }

        var oldestRead = int.MaxValue;

        foreach (var (path, entry) in known.OrderBy(k => k.Entry.Session.StartUtc))
        {
            var index = order.GetValueOrDefault(entry.SourceSessionId, int.MaxValue);
            var isActive = string.Equals(entry.SourceSessionId, _activeSourceId, StringComparison.Ordinal);
            var lastWrite = Infrastructure.LogDirectory.TryGetLastWriteTimeUtc(path);
            var recentlyWritten = lastWrite is not null && lastWrite.Value > now - _retention;

            // 自動リセットが無効なら、行は期限で消えない。消した区切りより後に書かれたログはすべて読む（→実装メモ5.108）。
            // 保存から戻したまま退出の分からない行も、ログが残っていればここで読んで退出とクラッシュを確かめる。
            var keptWritten = !_autoReset && lastWrite is not null && lastWrite.Value >= EffectiveCutoffUtc;

            // 読む必要のあるファイル: 実行中セッション、直近60分に書かれたファイル、
            // およびカウント期間の起点になり得る最新のファイル。
            if (!isActive && !recentlyWritten && !keptWritten && index < order.Count - 1)
                continue;

            ReadSession(entry.SourceSessionId);
            oldestRead = Math.Min(oldestRead, index);
        }

        // 対象を離れてから60分に達するまでは、どれだけ古い行も捨てない（2026-09-18のユーザー指定）。
        // 保持が続いている限り、60分より前に書かれたセッションもさかのぼって読む。
        var walkedBack = 0;

        while (oldestRead > 0 && oldestRead != int.MaxValue && walkedBack < MaxRetentionChainSessions)
        {
            if (_history.EarliestVisitUtc is not { } earliest)
                break;

            // 手元の行が既に期限切れなら、これより前を読んでも表示しない。
            // 前回までに消した区切り（保存してある→実装メモ5.45）より前も同じく読まない。
            // 自動リセットが無効なら期限はない（→実装メモ5.108。以前はここで期限を数えてしまい、さかのぼるのをやめていた）。
            var cutoff = _autoReset ? ComputeCutoff(CollectRetentionStarts(), now) : DateTime.MinValue;
            var removedBefore = EffectiveCutoffUtc;
            if (earliest < (cutoff > removedBefore ? cutoff : removedBefore))
                break;

            var previous = sessions[oldestRead - 1];
            if (!byId.TryGetValue(previous.SourceSessionId, out var previousItem))
                break;

            // 前のセッションでの退出は、そのファイルの最終書き込みより後にはならない。
            // そこから60分以内に対象へ戻っていなければ、履歴はそこで途切れている。
            var previousWrite = Infrastructure.LogDirectory.TryGetLastWriteTimeUtc(previousItem.Path);
            if (previousWrite is null || previousWrite.Value + _retention <= earliest)
                break;

            ReadSession(previous.SourceSessionId);
            oldestRead--;
            walkedBack++;
        }

        _historyTruncated = walkedBack >= MaxRetentionChainSessions;

        if (_historyTruncated)
            _log.Warn($"連続した起動が{MaxRetentionChainSessions}件を超えました。これより前のログは読みません。");

        // 第2段: カウント期間の起点を決める。対象は実行中セッション、なければ最新セッション。
        var targetId = _activeSourceId ?? sessions.LastOrDefault()?.SourceSessionId;
        var epochStartIndex = 0;

        if (targetId is not null && order.TryGetValue(targetId, out var targetIndex))
        {
            var resolution = ResolveEpoch(sessions, targetId, targetIndex, oldestRead);

            if (_counter.ApplyResolution(resolution))
                _log.Info($"カウント期間を更新: 起点={resolution.StartSessionId} 基準={(resolution.BaselineKnown ? "既知" : "不明")} 理由={resolution.Reason}");

            // 自動リセットで始めた期間の起点は、そのときのセッション（なければ時刻）。セッションの記録がなければ、時刻より後に始まった最初のセッションから数える。
            epochStartIndex = order.TryGetValue(resolution.StartSessionId, out var startIndex)
                ? startIndex
                : sessions.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.StartUtc >= resolution.StartUtc, (null!, targetIndex)).i;
        }

        foreach (var source in _sources.Values)
            source.CountingAllowed = source.SessionOrder >= epochStartIndex;

        // 第3段: 保留していた成功イベントへ、古い順に回数を付ける。
        var deferred = _deferredCounts;
        _deferredCounts = null;

        // このアプリが動いていない間に自動リセットの期限が来ていたら、そこで数え直してから、そのあとの訪問に回数を付ける（→実装メモ5.110）。
        // 期限の判定（ApplyRetention）は回数を付けたあとなので、先に区切りだけ見ておく。
        var resetAt = AutoResetCutoff(CollectRetentionStarts(), now);
        var resetPending = resetAt > _historyCutoffUtc && !CountingStartedAt(resetAt);

        foreach (var pending in deferred.Where(p => !resetPending || p.Record.VisitedAtUtc < resetAt))
            ApplyCount(pending.Source, pending.Record, pending.ByteEnd);

        if (resetPending)
        {
            StartCountingOver(resetAt);

            foreach (var pending in deferred.Where(p => p.Record.VisitedAtUtc >= resetAt))
                ApplyCount(pending.Source, pending.Record, pending.ByteEnd);
        }

        // 動いていないVRChatのログセッションは、そこで終わっている。
        // 正常終了のログがなければクラッシュとして、最後の時刻で退出を記録する（→5.30節）。
        foreach (var source in _sources.Values)
        {
            if (MayBelongToRunningClient(source))
                continue;

            FinalizeEndedSession(source, exitUtc: null);
        }

        CloseRestoredOpenVisits(now);
        ApplyRetention(now);

        if (_ambiguousClient)
            return;

        if (_activeSourceId is null && _client is not null)
            SetHealth(LogHealth.NoLogMatch, "実行中のVRChatに対応するログセッションをまだ特定できません。");
        else
            SetHealth(LogHealth.Ok, null);

        MarkDirty();
    }

    /// <summary>
    /// カウント期間（回数を数え直す区切り）を決める（2026-10-01のユーザー指定→実装メモ5.109・5.110）。
    ///
    /// | 自動リセット | 期間 |
    /// | --- | --- |
    /// | 無効 | VRChat の起動ごと。切り替えは次の起動から効く |
    /// | 有効 | 前回の自動リセットから。VRChat を起動し直しても引き継ぐ（前回の終わりから60分以内かは見ない）。自動リセットが入ったら数え直す（→<see cref="ApplyRetention"/>） |
    ///
    /// 有効でまだ期間がない（初めての起動・チェックポイントを読めない）か、基準の分からない期間（前の版の判定で決めたもの）なら、
    /// 読んだうちいちばん古い起動から数える。さかのぼって読むのは自動リセットの期限が来ていない間だけなので（→RestoreFromLogs）、
    /// それより前には自動リセットが入っている。読んだ範囲の中で入った自動リセットは、回数を付けるときに数え直す。
    /// </summary>
    private EpochResolution ResolveEpoch(IReadOnlyList<LogSessionInfo> sessions, string targetId, int targetIndex, int oldestRead)
    {
        var launch = sessions[targetIndex].StartUtc;

        if (_options.AssumeEpochStart)
            return new EpochResolution(targetId, launch, true, EpochReason.ExplicitEpochStart);

        if (!_autoReset)
            return new EpochResolution(targetId, launch, true, EpochReason.PerLaunch);

        if (_counter.Anchor is { } anchor && _counter.BaselineKnown && _counter.EpochId.Length > 0)
            return new EpochResolution(anchor.SourceSessionId, anchor.StartUtc, true, EpochReason.SinceAutoReset);

        var first = oldestRead < targetIndex ? sessions[oldestRead] : sessions[targetIndex];
        return new EpochResolution(first.SourceSessionId, first.StartUtc, true, EpochReason.FirstLaunch);
    }

    private OpenSource? OpenOrGetSource(string path, LogSourceEntry entry, int sessionOrder)
    {
        if (_sources.TryGetValue(entry.SourceSessionId, out var existing))
        {
            existing.Path = path;
            return existing;
        }

        var tracker = new VisitTracker(
            entry.SourceSessionId,
            sessionOrder,
            _time,
            _clock,
            _options.JoinWindow,
            _options.WorldNameWindow,
            _targets)
        {
            ShowDuringLoadingScreen = _showDuringLoading,
        };

        var source = new OpenSource
        {
            Entry = entry,
            Path = path,
            Tracker = tracker,
            SessionOrder = sessionOrder,
        };

        _sources[entry.SourceSessionId] = source;
        return source;
    }

    /// <summary>
    /// ソースを先頭から、または続きから読む。
    /// 復元では必ず先頭から再生して候補・現在地を組み直し、
    /// 保存済みの適用位置より前はカウンターへ再加算しない（仕様3.4節）。
    /// </summary>
    private void ReadSource(OpenSource source)
    {
        try
        {
            if (source.Reader is null)
            {
                // 復元では先頭から再生して候補・現在地を組み直す。閉じた続きから再開する場合だけ位置を使う。
                source.Reader = new LogFileReader(source.Path, source.ResumeOffset);
            }
            else if (source.Reader.IsTruncated)
            {
                _log.Warn($"ログが短縮されました: {source.Entry.FileName}。読み直します。");
                RebuildSource(source);
                return;
            }

            DateTime? lastLine = null;
            var read = 0;

            foreach (var line in source.Reader.ReadNewLines())
            {
                if (_options.StopAtUtc is { } stop
                    && line.HasValidTimestamp
                    && _time.TryToUtc(line.TimestampLocal, out var lineUtc)
                    && lineUtc > stop)
                {
                    break;
                }

                if (_options.OnLogTime is { } onLogTime
                    && line.HasValidTimestamp
                    && _time.TryToUtc(line.TimestampLocal, out var at))
                {
                    onLogTime(at);
                }

                ProcessLine(source, line);
                read++;

                if (line.HasValidTimestamp && _time.TryToUtc(line.TimestampLocal, out var written))
                    lastLine = written;
            }

            if (IsLive(source))
            {
                // 実行中の VRChat のログが増えた時刻と、読んだばかりの行の時刻のずれ（→実装メモ5.85）。
                // 起動時の全走査（復元中）の行は古いので、ずれには数えない。
                if (read > 0)
                    _lastAppendUtc = _clock.UtcNow;

                if (lastLine is { } fresh && _deferredCounts is null)
                    _clockSkew = _clock.UtcNow - fresh;

                // 読み取りの失敗・読み直しから戻った（→実装メモ5.85。それまでは戻らず、VRChat を起動し直すまで非表示のままだった）。
                if (_health is LogHealth.ReadError or LogHealth.Rebuilding && !_ambiguousClient)
                    SetHealth(LogHealth.Ok, null);
            }

            // ログの最後の行の時刻は、終わりの記録がないときの「終わりの下限」になる（→実装メモ5.60）。
            // 解析しない行（通信のエラーなど）も含める。VRChatはそれを書いた時点まで動いていた。
            // 読むたびに変わるので、これだけでは保存しない（ほかの保存に乗せる。ファイルがあれば起動のたびに読み直す）。
            if (lastLine is { } last
                && (source.Entry.Session.LastLogUtc is not { } known || last > known))
            {
                source.Entry.Session.LastLogUtc = last;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 単なる追記なしは正常。読み取り失敗だけを障害として扱い、非表示で再試行する。
            // 読み直しの準備をしても、状態は「読み取り失敗」のまま残す（読めるようになるまで理由を出す→実装メモ5.85）。
            _readFailure = LogReadFailures.Classify(ex);
            RebuildSource(source, announce: false);
            SetHealth(LogHealth.ReadError, $"ログを読めません: {ex.Message}");
        }
        catch (Exception ex)
        {
            // 行の解析・履歴の更新で起きた例外（プログラムの誤り）。ログの中の同じ行で毎回起きうるので、呼び出し元へ投げると
            // 主ループが止まり、起動のたびに同じ行で止まって起動できなくなる。読み取りの失敗と同じく非表示にして読み直しを待ち、
            // 原因が隠れないよう、スタックトレースごと記録する（同じ例外が続く間は1回だけ）。
            var signature = $"{source.Entry.SourceSessionId}|{ex.GetType().FullName}|{ex.Message}";

            if (signature != _lastProcessingError)
            {
                _lastProcessingError = signature;
                _log.Error($"ログの行を処理できませんでした（{Path.GetFileName(source.Path)}）。プログラムの誤りです: {ex}");
            }

            _readFailure = LogReadFailure.Other;
            RebuildSource(source, announce: false);
            SetHealth(LogHealth.ReadError, $"ログを処理できません: {ex.Message}");
        }
    }

    private void ProcessLine(OpenSource source, in LogLine line)
    {
        var ev = LogEventParser.Parse(line);
        if (ev.Kind == LogEventKind.Other)
            return;

        // VR の方式は状態の段に出すだけで、訪問・現在地には関わらない（→実装メモ5.85）。
        if (ev.Kind == LogEventKind.VrSdkStarted)
        {
            source.VrSdk = ev.Payload;
            return;
        }

        var previousState = source.Tracker.State;
        var previousTab = source.Tracker.MenuPageOpen;
        var record = source.Tracker.Apply(ev);

        if (ev.Kind == LogEventKind.ApplicationQuit && source.Tracker.QuitAtUtc is { } quit)
        {
            source.Entry.Session.QuitAtUtc = quit;
            _checkpointDirty = true;
        }

        if (record is not null)
        {
            var isNew = _history.Add(record);

            // 復元中は、カウント期間の起点が決まるまで加算を保留する。
            if (_deferredCounts is not null)
                _deferredCounts.Add(new PendingCount(source, record, line.ByteEnd));
            else
                ApplyCount(source, record, line.ByteEnd);

            if (isNew)
            {
                VisitAdded?.Invoke(record);
                MarkDirty();
            }
        }

        // 記録しない種類の訪問も、回数だけは数える（行にはしない→実装メモ5.58）。
        if (source.Tracker.LastExcludedVisit is { } excluded)
        {
            if (_deferredCounts is not null)
                _deferredCounts.Add(new PendingCount(source, excluded, line.ByteEnd));
            else
                ApplyCount(source, excluded, line.ByteEnd);
        }

        // 対象インスタンスを離れたら、その訪問の行へ退出時刻を書き戻す。
        if (source.Tracker.LastLeave is { } leave && _history.SetLeave(leave.EventId, leave.AtUtc))
            MarkDirty();

        // 退出直後に並ぶ OnPlayerLeft を数えた結果を、その訪問の行へ書き戻す。
        if (source.Tracker.LastPeopleCount is { } people && _history.SetPeopleCount(people.EventId, people.Count))
            MarkDirty();

        // 対象外のインスタンスへ移ったら、その前に入っていた対象訪問の行へ印を付ける（→5.38節）。
        if (source.Tracker.LastExcludedAfter is { } excludedAfter && _history.SetExcludedAfter(excludedAfter))
            MarkDirty();

        // 滞在中に出入りした人（→5.46節）と撮った写真（→5.47節）を、その訪問の行へ書き足す。
        if (source.Tracker.LastCompanion is { } companion && _history.SetCompanion(companion.EventId, companion.Companion))
            MarkDirty();

        if (source.Tracker.LastPhoto is { } photo && _history.AddPhoto(photo.EventId, photo.Photo))
            MarkDirty();

        // メインメニューを閉じたログで閉じたとき（→5.104節）。B / Y で閉じたときと見分けられるよう、--verbose で残す。
        // 起動時の読み直しの間は、過去の開け閉めを並べるだけなので出さない。
        if (ev.Kind == LogEventKind.MainMenuPageHiding && previousTab && !source.Tracker.MenuPageOpen && _deferredCounts is null)
            _log.Info($"メインメニューを閉じたログ（{ev.Payload}）で、パネルを閉じました。");

        if (source.Tracker.State != previousState || source.Tracker.MenuPageOpen != previousTab)
            MarkDirty();
    }

    /// <summary>成功イベントへ回数を付け、適用位置を進める。保存はこの単位で行う。</summary>
    private void ApplyCount(OpenSource source, VisitRecord record, long byteEnd)
    {
        var counted = _counter.Apply(record, source.Entry.AppliedOffset, source.CountingAllowed);

        if (!counted)
            return;

        // 適用位置は「処理済みの完全な行の直後」。
        source.Entry.AppliedOffset = Math.Max(source.Entry.AppliedOffset, byteEnd);
        _checkpointDirty = true;
    }

    private void TailActiveSource()
    {
        if (_activeSourceId is null || !_sources.TryGetValue(_activeSourceId, out var source))
            return;

        ReadSource(source);
        SaveCheckpointIfNeeded(force: false);
    }

    /// <summary>
    /// 短縮・差し替え・読み取り失敗からの再構築。先頭から読み直し、現在地と候補を解除する。
    /// 適用済み位置は保持したままにして、回数の二重加算を避ける。
    /// </summary>
    /// <param name="announce">状態を「読み直し中」にするか。読み取りの失敗から読み直すときは、失敗のほうを出し続ける（→実装メモ5.85）。</param>
    private void RebuildSource(OpenSource source, bool announce = true)
    {
        if (announce)
            SetHealth(LogHealth.Rebuilding, $"{source.Entry.FileName} を読み直します。");
        CloseReader(source);
        source.ResumeOffset = 0;
        source.Tracker.ResetForNewSession();
        source.Replayed = false;

        // 旧派生レコードを残したまま再追加しない。回数は eventId の重複除去で守る。
        if (_history.RemoveBySource(source.Entry.SourceSessionId) > 0)
            MarkDirty();
    }

    private void CloseReader(OpenSource source)
    {
        if (source.Reader is not null)
            source.ResumeOffset = source.Reader.Position;

        source.Reader?.Dispose();
        source.Reader = null;
    }

    // ------------------------------------------------------------------ 終了・クラッシュ

    /// <summary>
    /// 終わったログセッションの後始末（2026-09-21のユーザー指定→5.30節）。
    ///
    /// VRChatがクラッシュすると、対象インスタンスにいたことを示す行だけが残り、
    /// 退出のログ（Destination set / OnLeftRoom / HandleApplicationQuit）は書かれない。
    /// そのままでは「入室したきり」の行になり、退出時刻も人数も入らない。
    ///
    /// そこで、プロセスの消滅かログの途切れでセッションの終わりが分かった時点で、
    /// 滞在したままの訪問へ退出時刻と人数を入れる。正常終了の記録
    /// （<see cref="LogSessionInfo.QuitAtUtc"/>）がなければ、その終わり方をクラッシュとして印を付け、
    /// 表示側が退出時刻を赤くし、次の行との間に帯を入れられるようにする。
    ///
    /// 退出時刻は、はっきりしているものから順に
    /// プロセスの終了時刻 → ログの最後の行の時刻 → 現在時刻 を使う。
    /// </summary>
    /// <param name="exitUtc">プロセスの消滅を見て呼ぶ場合のその時刻。ログだけから判断する場合は null。</param>
    private void FinalizeEndedSession(OpenSource source, DateTime? exitUtc)
    {
        var crashed = source.Entry.Session.QuitAtUtc is null;

        // このアプリが動いている間に、プロセスの終了で気づいたクラッシュだけを状態の段に出す（→実装メモ5.85）。
        // 対象インスタンスにいなかったとき（行に印を付けないとき）も出す。
        if (crashed && exitUtc is not null)
        {
            _crashDetectedUtc = _clock.UtcNow;
            _log.Info("VRChatが正常終了の記録を残さずに終わりました（クラッシュ・強制終了など）。状態の段に1分間出します。");
        }

        var at = exitUtc
            ?? source.Entry.Session.ProcessExitUtc
            ?? EstimateEndFromLog(source)
            ?? _clock.UtcNow;

        // 対象インスタンスにいなければ、書き戻す行がない（状態だけが終了になる）。
        if (source.Tracker.EndSession(at) is not { } leave)
            return;

        var changed = _history.SetLeave(leave.EventId, leave.AtUtc);

        if (source.Tracker.LastPeopleCount is { } people)
            changed |= _history.SetPeopleCount(people.EventId, people.Count);

        if (crashed)
        {
            changed |= _history.SetCrashed(leave.EventId);
            _log.Warn($"VRChatが正常終了の記録を残さずに終わりました（クラッシュ）。{LocalClock(at)} を退出時刻として記録します。");
        }

        if (changed)
            MarkDirty();
    }

    /// <summary>
    /// ログだけから推し測る「終わった時刻」。
    ///
    /// クラッシュの直前まで書かれ続けるので、ファイルの最終更新時刻（＝ログが伸びなくなった時刻）が
    /// 実際の終わりにいちばん近い。解析できる行は数分に1回とは限らないため、
    /// 最後に読めたイベントの時刻よりこちらを優先する。
    ///
    /// ただし、ファイルの日時は複製などで実際より後ろへずれることがあり、
    /// 消えたファイルでは既定値（1601年）が返る。現在時刻より先、セッションの開始より前、
    /// 最後に読めた行より前の値はどれも採らない。
    /// </summary>
    private DateTime? EstimateEndFromLog(OpenSource source)
    {
        var lastEvent = source.Tracker.LastEventAtUtc;
        var written = Infrastructure.LogDirectory.TryGetLastWriteTimeUtc(source.Path);

        if (written is { } at
            && at <= _clock.UtcNow + VisitTracker.FutureTolerance
            && at >= source.Entry.Session.SessionStartUtc
            && (lastEvent is null || at >= lastEvent))
        {
            return at;
        }

        return lastEvent;
    }

    /// <summary>
    /// そのログセッションが、いま動いているVRChatのものかもしれないか。
    ///
    /// 動いているかもしれないセッションを終わったものとして扱うと、滞在中の行に
    /// 退出時刻とクラッシュの印を付けてしまう。プロセスの対応づけ（<see cref="SelectActiveSource"/>）
    /// と同じ幅で見て、少しでも重なるセッションは終わったことにしない。
    /// </summary>
    private bool MayBelongToRunningClient(OpenSource source)
        => MayBelongToRunningClient(source.Entry.Session.SessionStartUtc);

    /// <summary>その時刻に始まったログセッションが、いま動いているVRChatのものかもしれないか（→<see cref="MayBelongToRunningClient(OpenSource)"/>）。</summary>
    private bool MayBelongToRunningClient(DateTime start)
    {
        foreach (var process in _processes.Running)
        {
            if (start >= process.StartUtc - _options.ProcessMatchBefore
                && start <= process.StartUtc + _options.ProcessMatchAfter)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>復元中に保留した成功イベント。</summary>
    private readonly record struct PendingCount(OpenSource Source, VisitRecord Record, long ByteEnd);

    private sealed class OpenSource
    {
        public required LogSourceEntry Entry { get; init; }

        public required string Path { get; set; }

        public required VisitTracker Tracker { get; init; }

        public required int SessionOrder { get; init; }

        public LogFileReader? Reader { get; set; }

        /// <summary>閉じたリーダーを開き直す位置。再構築時は0へ戻す。</summary>
        public long ResumeOffset { get; set; }

        public bool Replayed { get; set; }

        public bool CountingAllowed { get; set; } = true;

        /// <summary>このセッションの VR の方式（ログの <c>StartVRSDK:</c>→実装メモ5.85）。</summary>
        public string? VrSdk { get; set; }
    }
}
