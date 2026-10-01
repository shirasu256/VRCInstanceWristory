using VRCInstanceWristory.Core;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>アップデートの段階（→実装メモ5.121）。</summary>
public enum UpdatePhase
{
    /// <summary>インストーラーで入れた版ではない（dist・自分でビルドしたもの）。自動更新は使えない。</summary>
    Unavailable,

    /// <summary>まだ確かめていない。</summary>
    Idle,

    Checking,

    /// <summary>最新の版を使っている。</summary>
    UpToDate,

    /// <summary>新しい版がある（<see cref="UpdateStatus.Version"/>）。</summary>
    Available,

    Downloading,

    /// <summary>落とし終えた。終了して入れ替えるのを待っている。</summary>
    Ready,

    /// <summary>確かめる・落とすのに失敗した（<see cref="UpdateStatus.Error"/>）。</summary>
    Failed,
}

/// <summary>画面に出すアップデートの状態。同じ値なら描き直さないので、進み具合は1%刻みにとどめる。</summary>
public sealed record UpdateStatus(
    UpdatePhase Phase,
    string? Version = null,
    int Progress = 0,
    DateTime? CheckedAtUtc = null,
    string? Error = null)
{
    public static readonly UpdateStatus Unavailable = new(UpdatePhase.Unavailable);

    /// <summary>新しい版があることを知らせる場面か（状態の段のリンクを出す）。</summary>
    public bool Offering => Phase is UpdatePhase.Available or UpdatePhase.Downloading or UpdatePhase.Ready;
}

/// <summary>
/// アップデートの仕組みそのもの（Velopack→<see cref="VelopackUpdateBackend"/>）。自動検証では差し替える。
/// どのメソッドも、ほかのスレッドから呼ばれる（<see cref="AppUpdater"/> が主ループを止めないよう別のスレッドで呼ぶ）。
/// </summary>
public interface IUpdateBackend
{
    /// <summary>インストーラーで入れた版として動いているか。</summary>
    bool IsInstalled { get; }

    /// <summary>新しい版を確かめる。あればその版（"0.2.0" の形）、なければ null。</summary>
    Task<string?> CheckAsync(CancellationToken cancel);

    /// <summary><see cref="CheckAsync"/> で見つけた版を落とす。<paramref name="progress"/> は 0〜100。</summary>
    Task DownloadAsync(Action<int> progress, CancellationToken cancel);

    /// <summary>
    /// 落とした版を、このプロセスが終わったあとで入れ替えて起動し直すよう頼む。呼んだら、すぐに（保存を済ませて）終わる。
    /// </summary>
    void ApplyAfterExit();
}

/// <summary>
/// アップデートの確認・ダウンロード・入れ替えの段取り（2026-10-01のユーザー指定→実装メモ5.121）。
///
/// 状態を変えるのは主ループ（<see cref="Poll"/>・<see cref="CheckNow"/>・<see cref="RequestApply"/>）と、確認・ダウンロードの終わり（別のスレッド）。
/// 主ループは毎フレーム <see cref="Poll"/> を呼び、状態が変わったら画面へ知らせる。
///
/// | 場面 | すること |
/// | --- | --- |
/// | 起動から <see cref="FirstCheckDelay"/> 後・前の確認から <see cref="CheckInterval"/> 後 | 自動で確かめる（設定 <c>updateCheckEnabled</c> がオンのときだけ） |
/// | 「今すぐ確認」 | 設定によらず確かめる |
/// | 「更新して再起動」（確かめる画面で「はい」） | まだなら落とし、落とし終えたら <see cref="TakeApplyNow"/> が true になる。主ループは入れ替えを頼んで終わる |
///
/// 落とすのは利用者が押したときだけ（使っていない回線を勝手に使わない）。入れ替えも押したときだけで、VRChat の最中に勝手に起動し直さない。
/// </summary>
public sealed class AppUpdater
{
    /// <summary>起動してから最初に自動で確かめるまで（起動の直後は SteamVR やログの読み込みで忙しいので、少し待つ）。</summary>
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);

    /// <summary>自動で確かめる間隔。</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

    private readonly IUpdateBackend? _backend;
    private readonly IClock _clock;
    private readonly IDiagnostics _log;
    private readonly DateTime _startedUtc;
    private readonly object _gate = new();

    private UpdateStatus _status;
    private UpdateStatus? _published;
    private DateTime? _lastCheckUtc;
    private bool _busy;
    private bool _applyWhenReady;
    private bool _applyTaken;

    public AppUpdater(IUpdateBackend? backend, IClock clock, IDiagnostics log)
    {
        _backend = backend is { IsInstalled: true } ? backend : null;
        _clock = clock;
        _log = log;
        _startedUtc = clock.UtcNow;
        _status = _backend is null ? UpdateStatus.Unavailable : new UpdateStatus(UpdatePhase.Idle);
    }

    /// <summary>いまの状態。</summary>
    public UpdateStatus Status
    {
        get
        {
            lock (_gate)
                return _status;
        }
    }

    /// <summary>
    /// 主ループから毎フレーム呼ぶ。自動で確かめる時刻なら確かめ始める。
    /// 画面へ知らせていない変化があれば、その状態を返す（なければ null）。
    /// </summary>
    public UpdateStatus? Poll(bool autoCheck)
    {
        if (autoCheck && DueForCheck())
            StartCheck(manual: false);

        lock (_gate)
        {
            if (_status == _published)
                return null;

            _published = _status;
            return _status;
        }
    }

    /// <summary>「今すぐ確認」。確かめている・落としている最中なら何もしない。</summary>
    public void CheckNow() => StartCheck(manual: true);

    /// <summary>
    /// 「更新して再起動」。新しい版があれば落とし始め、落とし終えたら <see cref="TakeApplyNow"/> が true になる。
    /// 落とし終えていれば、すぐに true になる。
    /// </summary>
    public void RequestApply()
    {
        lock (_gate)
        {
            if (_backend is null || _status.Phase is not (UpdatePhase.Available or UpdatePhase.Downloading or UpdatePhase.Ready))
                return;

            _applyWhenReady = true;

            if (_status.Phase != UpdatePhase.Available || _busy)
                return;

            _busy = true;
            _status = _status with { Phase = UpdatePhase.Downloading, Progress = 0 };
        }

        _ = Task.Run(DownloadAsync);
    }

    /// <summary>入れ替えてよい（落とし終えていて、利用者が頼んだ）か。true を返すのは1回だけ。</summary>
    public bool TakeApplyNow()
    {
        lock (_gate)
        {
            if (_applyTaken || !_applyWhenReady || _status.Phase != UpdatePhase.Ready)
                return false;

            _applyTaken = true;
            return true;
        }
    }

    /// <summary>入れ替えを頼む（<see cref="TakeApplyNow"/> が true を返したあと、終わる直前に呼ぶ）。</summary>
    public void ApplyAfterExit()
    {
        try
        {
            _backend?.ApplyAfterExit();
            _log.Notice($"アップデート: 終了したあとで v{Status.Version} へ入れ替えて起動し直します。");
        }
        catch (Exception ex)
        {
            _log.Error($"アップデートを入れ替えられません: {ex.Message}");
        }
    }

    private bool DueForCheck()
    {
        lock (_gate)
        {
            if (_backend is null || _busy || _status.Phase is UpdatePhase.Downloading or UpdatePhase.Ready)
                return false;

            var now = _clock.UtcNow;
            return _lastCheckUtc is { } last ? now - last >= CheckInterval : now - _startedUtc >= FirstCheckDelay;
        }
    }

    private void StartCheck(bool manual)
    {
        lock (_gate)
        {
            if (_backend is null || _busy || _status.Phase is UpdatePhase.Downloading or UpdatePhase.Ready)
                return;

            _busy = true;
            _lastCheckUtc = _clock.UtcNow;
            _status = _status with { Phase = UpdatePhase.Checking, Error = null };
        }

        _ = Task.Run(() => CheckAsync(manual));
    }

    private async Task CheckAsync(bool manual)
    {
        try
        {
            var version = await _backend!.CheckAsync(CancellationToken.None).ConfigureAwait(false);

            lock (_gate)
            {
                _status = version is null
                    ? new UpdateStatus(UpdatePhase.UpToDate, CheckedAtUtc: _clock.UtcNow)
                    : new UpdateStatus(UpdatePhase.Available, version, CheckedAtUtc: _clock.UtcNow);
            }

            if (version is not null)
                _log.Notice($"アップデート: 新しい版 v{version} があります。");
            else if (manual)
                _log.Info("アップデート: 最新の版です。");
        }
        catch (Exception ex)
        {
            lock (_gate)
                _status = new UpdateStatus(UpdatePhase.Failed, CheckedAtUtc: _clock.UtcNow, Error: ex.Message);

            _log.Warn($"アップデートを確かめられません: {ex.Message}");
        }
        finally
        {
            lock (_gate)
                _busy = false;
        }
    }

    private async Task DownloadAsync()
    {
        try
        {
            await _backend!.DownloadAsync(
                percent =>
                {
                    lock (_gate)
                    {
                        if (_status.Phase == UpdatePhase.Downloading)
                            _status = _status with { Progress = Math.Clamp(percent, 0, 100) };
                    }
                },
                CancellationToken.None).ConfigureAwait(false);

            lock (_gate)
                _status = _status with { Phase = UpdatePhase.Ready, Progress = 100 };
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _applyWhenReady = false;
                _status = _status with { Phase = UpdatePhase.Failed, Error = ex.Message };
            }

            _log.Warn($"アップデートを落とせません: {ex.Message}");
        }
        finally
        {
            lock (_gate)
                _busy = false;
        }
    }
}
