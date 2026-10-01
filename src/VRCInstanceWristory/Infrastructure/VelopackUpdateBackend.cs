using Velopack;
using Velopack.Sources;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// Velopack による更新（→実装メモ5.121）。GitHub のリポジトリの Releases（<see cref="AppInfo.RepositoryUrl"/>）から、
/// 正式な版（プレリリースを除く）だけを探す。GitHub へは認証なしで問い合わせる（1時間に60回まで。12時間に1回なので足りる）。
/// </summary>
public sealed class VelopackUpdateBackend : IUpdateBackend
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _found;

    public VelopackUpdateBackend()
    {
        _manager = new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: false));
    }

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<string?> CheckAsync(CancellationToken cancel)
    {
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken cancel)
        => _found is { } found
            ? _manager.DownloadUpdatesAsync(found, progress, cancel)
            : throw new InvalidOperationException("先に新しい版を確かめてください。");

    public void ApplyAfterExit()
    {
        if (_found is not { } found)
            throw new InvalidOperationException("落とした版がありません。");

        // 更新を入れる画面（進み具合）は出す。入れ替えたら、引数なしで起動し直す（ウィンドウを出して始める）。
        _manager.WaitExitThenApplyUpdates(found.TargetFullRelease, silent: false, restart: true, restartArgs: []);
    }
}
