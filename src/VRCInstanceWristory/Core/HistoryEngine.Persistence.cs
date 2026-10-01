using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Core;

// 目印（marks.json）・チェックポイント（counter-state.json）・訪問履歴（history.json）の読み書き。
public sealed partial class HistoryEngine
{
    // ------------------------------------------------------------------ 目印（→実装メモ5.32・5.54）

    /// <summary>
    /// 保存してあった目印を読む。チェックポイント（消去の区切り）を読んだあとに呼ぶ。
    /// アプリを止めている間にログのリセットがあれば、その前に付けた目印はここで消える。
    /// </summary>
    private void LoadMarks()
    {
        if (_markFile is null)
            return;

        _marks.Load(_markFile.Load());

        if (_markFile.LastError is { } error)
            _log.Warn($"目印の保存を読めません（目印なしで始めます）: {error}");

        if (_marks.RemoveMarkedBefore(_historyCutoffUtc))
            SaveMarks();
    }

    private void SaveMarks()
    {
        if (_markFile is null)
            return;

        _marksSaveFailed = !_markFile.Save(_marks.Entries());

        if (_marksSaveFailed)
            _log.Error($"目印を保存できません: {_markFile.LastError}");
    }

    // ------------------------------------------------------------------ 訪問履歴（→実装メモ5.108）

    /// <summary>
    /// 保存してあった訪問履歴を戻す。チェックポイント（消去の区切り・ソースIDの台帳）を読んだあと、ログを読む前に呼ぶ。
    ///
    /// 戻すのは自動リセットが無効のときだけ。有効のときは従来どおりログから組み直す
    /// （退出の記録がログにしかないので、戻した行の期限を正しく数えられない）。
    /// ログを読めた訪問は、読んだ行へ置き換わる（<see cref="HistoryStore.Add"/>）。
    /// </summary>
    private void LoadHistory()
    {
        if (_historyFile is null || _autoReset)
            return;

        if (_historyFile.Load() is not { } saved)
        {
            if (_historyFile.LastError is { } error)
                _log.Warn($"訪問履歴の保存を読めません（ログから組み直します）: {error}");

            return;
        }

        // 消した区切りより前と、時計が戻っていて「これから先」に見える行は戻さない。
        var cutoff = EffectiveCutoffUtc;
        var limit = _clock.UtcNow + VisitTracker.FutureTolerance;
        var records = saved.Records.Where(r => r.VisitedAtUtc >= cutoff && r.VisitedAtUtc <= limit).ToList();

        // 台帳から捨てたソースのIDを、新しいソースへ使い回さない（同じ eventId の別の行になってしまう）。
        foreach (var r in records)
            _registry.ReserveId(r.SourceSessionId);

        var restored = _history.Restore(records);
        _restoredSavedAtUtc = saved.SavedAtUtc;
        _log.Info($"保存してあった訪問履歴を{restored}件戻しました（自動リセットが無効のため）。");
    }

    /// <summary>
    /// 行が変わっていれば訪問履歴を書く。<paramref name="force"/> でなければ <see cref="EngineOptions.HistorySaveInterval"/> ごとまで。
    /// 自動リセットが有効の間も書いておく（無効へ切り替えた直後に終了しても、その時点の行が残るように）。
    /// </summary>
    private void SaveHistoryIfNeeded(bool force)
    {
        if (_historyFile is null || _history.Version == _historySavedVersion)
            return;

        var now = _clock.UtcNow;

        if (!force && now - _historySavedUtc < _options.HistorySaveInterval)
            return;

        _historySavedUtc = now;

        if (_historyFile.Save(_history.Records, now))
        {
            _historySavedVersion = _history.Version;

            if (_historySaveFailed)
                _log.Info("訪問履歴の保存に復帰しました。");

            _historySaveFailed = false;
        }
        else
        {
            // 失敗しても次の間隔で試し直す（毎フレームは試さない）。
            if (!_historySaveFailed)
                _log.Error($"訪問履歴を保存できません（再起動すると、ログの消えた行は戻りません）: {_historyFile.LastError}");

            _historySaveFailed = true;
        }
    }

    /// <summary>
    /// 保存から戻したまま退出時刻のない行に、終わりを入れる（→実装メモ5.108）。ログを読んだあと、期限の判定の前に呼ぶ。
    ///
    /// そのログを読めていれば、ログから読んだ行へ置き換わっているので、ここに残るのはログが消えた（読めない）訪問だけである。
    /// 終わりは、分かっているものから順に そのセッションの終了・正常終了のログ・最後のログの時刻・保存した時刻。
    /// いま動いている VRChat のものかもしれない行は、ログの対応づけを待つので触らない。
    /// </summary>
    private void CloseRestoredOpenVisits(DateTime nowUtc)
    {
        foreach (var record in _history.RestoredRecords.Where(r => r.LeftAtUtc is null).ToList())
        {
            var session = _registry.Find(record.SourceSessionId)?.Session;

            if (session is not null && MayBelongToRunningClient(session.SessionStartUtc))
                continue;

            var end = session?.ProcessExitUtc ?? session?.QuitAtUtc ?? session?.LastLogUtc ?? _restoredSavedAtUtc ?? nowUtc;

            if (end < record.VisitedAtUtc)
                end = record.VisitedAtUtc;

            if (_history.SetLeave(record.EventId, end))
                MarkDirty();
        }
    }

    // ------------------------------------------------------------------ 保存

    private void LoadCheckpoint()
    {
        if (_checkpoint is null)
            return;

        var dto = _checkpoint.Load();
        if (dto is null)
        {
            if (_checkpoint.LastError is { } error)
            {
                _checkpointUnreadableUtc = _clock.UtcNow;
                _log.Warn($"チェックポイントを読めません（ログから再構築します）: {error}");
            }

            return;
        }

        foreach (var s in dto.Sources)
            _registry.Add(s.ToEntry());

        _counter.Restore(
            dto.EpochId ?? string.Empty,
            dto.BaselineKnown,
            dto.Anchor?.ToAnchor(),
            dto.Counts,
            dto.RecentEvents.Select(e => e.ToEvent()));

        // 「延長」を押した時刻と、消去の区切り（→実装メモ5.45）。
        // 時計が戻っていて「これから先」に見える値は採らない（未来の区切りは、これから入る行まで消してしまう）。
        var limit = _clock.UtcNow + VisitTracker.FutureTolerance;

        _retentionResets.Clear();
        _retentionResets.AddRange((dto.RetentionResets ?? []).Where(r => r <= limit).Order());

        if (dto.HistoryCutoffUtc is { } cutoff && cutoff <= limit && cutoff > _historyCutoffUtc)
            _historyCutoffUtc = cutoff;

        if (dto.AutoResetFromUtc is { } from && from <= limit)
            _autoResetFromUtc = from;

        if (dto.HistoryLimitCutoffUtc is { } capped && capped <= limit && capped > _limitCutoffUtc)
            _limitCutoffUtc = capped;

        _log.Info($"チェックポイントを読み込みました: ソース{dto.Sources.Count}件 / 回数{dto.Counts.Count}件 / 直近イベント{dto.RecentEvents.Count}件 / 延長{_retentionResets.Count}件");
    }

    private void SaveCheckpointIfNeeded(bool force)
    {
        if (_checkpoint is null || !_options.PersistCheckpoint)
            return;

        if (!_checkpointDirty && !force)
            return;

        var dto = new CheckpointDto
        {
            EpochId = _counter.EpochId,
            BaselineKnown = _counter.BaselineKnown,
            Anchor = _counter.Anchor is { } anchor ? CheckpointDto.AnchorDto.From(anchor) : null,
            Counts = new Dictionary<string, int>(_counter.Counts, StringComparer.Ordinal),
            Sources = _registry.Entries.Select(CheckpointDto.SourceDto.From).ToList(),
            RecentEvents = _counter.Events.Values.Select(CheckpointDto.EventDto.From).ToList(),
            RetentionResets = [.. _retentionResets],
            HistoryCutoffUtc = _historyCutoffUtc == DateTime.MinValue ? null : _historyCutoffUtc,
            AutoResetFromUtc = _autoResetFromUtc == DateTime.MinValue ? null : _autoResetFromUtc,
            HistoryLimitCutoffUtc = _limitCutoffUtc == DateTime.MinValue ? null : _limitCutoffUtc,
            SavedAtUtc = _clock.UtcNow,
        };

        if (_checkpoint.Save(dto))
        {
            _checkpointDirty = false;

            if (!_checkpointHealthy)
                _log.Info("チェックポイントの保存に復帰しました。");

            _checkpointHealthy = true;
        }
        else
        {
            _checkpointHealthy = false;
            _log.Error($"チェックポイントを保存できません（再起動時の復元に影響します）: {_checkpoint.LastError}");
        }
    }
}
