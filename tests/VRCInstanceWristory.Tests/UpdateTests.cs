using System.Drawing;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// インストーラー（Velopack）での更新とアンインストール（→実装メモ5.121）。
/// GitHub へは問い合わせない（更新の仕組みは <see cref="FakeUpdateBackend"/> に差し替える）。
/// 本物のスタートアップにも触れない（<see cref="FakeStartupRegistry"/>）。
/// </summary>
public class UpdateTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 7, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------------ 段取り（AppUpdater）

    [Fact]
    public void インストーラーで入れた版でなければ使えず_確かめもしない()
    {
        var backend = new FakeUpdateBackend { IsInstalled = false };
        var clock = new ManualClock(Start);
        var updater = new AppUpdater(backend, clock, new CollectingDiagnostics());

        Assert.Equal(UpdatePhase.Unavailable, updater.Status.Phase);

        clock.Advance(TimeSpan.FromHours(1));
        updater.Poll(autoCheck: true);
        updater.CheckNow();

        Assert.Equal(0, backend.CheckCount);
        Assert.Equal(UpdatePhase.Unavailable, new AppUpdater(null, clock, new CollectingDiagnostics()).Status.Phase);
    }

    [Fact]
    public void 起動の直後に自動で確かめ_新しい版を知らせる()
    {
        var backend = new FakeUpdateBackend { NextVersion = "0.2.0" };
        var clock = new ManualClock(Start);
        var updater = new AppUpdater(backend, clock, new CollectingDiagnostics());

        // 起動してすぐ「確認中」。「まだ確認していません」の状態はない。
        Assert.Equal(UpdatePhase.Checking, updater.Status.Phase);
        Assert.Equal(UpdatePhase.Checking, updater.Poll(autoCheck: true)!.Phase);
        WaitFor(() => updater.Status.Phase == UpdatePhase.Available);

        var status = updater.Poll(autoCheck: true)!;
        Assert.Equal("0.2.0", status.Version);
        Assert.True(status.Offering);
        Assert.Equal(1, backend.CheckCount);

        // 変わっていなければ知らせない。
        Assert.Null(updater.Poll(autoCheck: true));
    }

    [Fact]
    public void 自動で確かめるのをオフにしても今すぐ確認では確かめる()
    {
        var backend = new FakeUpdateBackend();
        var clock = new ManualClock(Start);
        var updater = new AppUpdater(backend, clock, new CollectingDiagnostics(), autoCheck: false);

        Assert.Equal(UpdatePhase.Idle, updater.Status.Phase);

        clock.Advance(TimeSpan.FromHours(1));
        updater.Poll(autoCheck: false);
        Assert.Equal(0, backend.CheckCount);

        updater.CheckNow();
        WaitFor(() => updater.Status.Phase == UpdatePhase.UpToDate);
        Assert.Equal(1, backend.CheckCount);
        Assert.Equal(clock.UtcNow, updater.Status.CheckedAtUtc);
    }

    [Fact]
    public void 前の確認から12時間たつと確かめ直す()
    {
        var backend = new FakeUpdateBackend();
        var clock = new ManualClock(Start);
        var updater = new AppUpdater(backend, clock, new CollectingDiagnostics());

        updater.Poll(autoCheck: true);
        WaitFor(() => updater.Status.Phase == UpdatePhase.UpToDate);

        clock.Advance(AppUpdater.CheckInterval - TimeSpan.FromMinutes(1));
        updater.Poll(autoCheck: true);
        Assert.Equal(1, backend.CheckCount);

        clock.Advance(TimeSpan.FromMinutes(1));
        updater.Poll(autoCheck: true);
        WaitFor(() => backend.CheckCount == 2 && updater.Status.Phase == UpdatePhase.UpToDate);
    }

    [Fact]
    public void 確かめられなければ失敗として理由を残す()
    {
        var backend = new FakeUpdateBackend { CheckError = new HttpRequestException("ネットワークにつながりません") };
        var updater = new AppUpdater(backend, new ManualClock(Start), new CollectingDiagnostics());

        updater.CheckNow();
        WaitFor(() => updater.Status.Phase == UpdatePhase.Failed);

        Assert.Equal("ネットワークにつながりません", updater.Status.Error);
        Assert.False(updater.Status.Offering);
    }

    [Fact]
    public void 更新して再起動を頼むと落とし_落とし終えたら1回だけ入れ替えてよいと返す()
    {
        var backend = new FakeUpdateBackend { NextVersion = "0.2.0" };
        var updater = new AppUpdater(backend, new ManualClock(Start), new CollectingDiagnostics());

        // 新しい版を見つける前は、頼んでも落とさない。
        updater.RequestApply();
        Assert.Equal(0, backend.DownloadCount);

        updater.CheckNow();
        WaitFor(() => updater.Status.Phase == UpdatePhase.Available);
        Assert.False(updater.TakeApplyNow());

        updater.RequestApply();
        WaitFor(() => backend.DownloadStarted);
        Assert.Equal(UpdatePhase.Downloading, updater.Status.Phase);

        backend.ReportProgress(42);
        WaitFor(() => updater.Status.Progress == 42);
        Assert.False(updater.TakeApplyNow());

        backend.FinishDownload();
        WaitFor(() => updater.Status.Phase == UpdatePhase.Ready);

        Assert.True(updater.TakeApplyNow());
        Assert.False(updater.TakeApplyNow());
        Assert.Equal(1, backend.DownloadCount);

        // 入れ替えを頼むのは、終わる直前（LiveMode）。
        Assert.Equal(0, backend.ApplyCount);
        updater.ApplyAfterExit();
        Assert.Equal(1, backend.ApplyCount);
    }

    [Fact]
    public void 落とし終えてもログの読み込みが終わるまでは待ち_落ち着いたら入れ替えてよいと返す()
    {
        var backend = new FakeUpdateBackend { NextVersion = "0.2.0" };
        var updater = new AppUpdater(backend, new ManualClock(Start), new CollectingDiagnostics());

        updater.CheckNow();
        WaitFor(() => updater.Status.Phase == UpdatePhase.Available);

        updater.RequestApply();
        WaitFor(() => backend.DownloadStarted);
        backend.FinishDownload();
        WaitFor(() => updater.Status.Phase == UpdatePhase.Ready);

        // 読み込み中は待つ（落とし終えた直後の取り出しでも渡さない）。
        Assert.False(updater.TakeApplyNow(logReady: false));
        Assert.Equal(UpdatePhase.Waiting, updater.Poll(autoCheck: false, logReady: false)!.Phase);
        Assert.True(updater.Status.Offering);
        Assert.False(updater.TakeApplyNow(logReady: false));
        Assert.False(updater.TakeApplyNow());

        // 落ち着いたら入れ替える。
        Assert.Equal(UpdatePhase.Ready, updater.Poll(autoCheck: false, logReady: true)!.Phase);
        Assert.True(updater.TakeApplyNow());
    }

    [Fact]
    public void 落とせなければ失敗にして入れ替えない()
    {
        var backend = new FakeUpdateBackend { NextVersion = "0.2.0" };
        var updater = new AppUpdater(backend, new ManualClock(Start), new CollectingDiagnostics());

        updater.CheckNow();
        WaitFor(() => updater.Status.Phase == UpdatePhase.Available);

        updater.RequestApply();
        WaitFor(() => backend.DownloadStarted);
        backend.FailDownload(new IOException("書き込めません"));

        WaitFor(() => updater.Status.Phase == UpdatePhase.Failed);
        Assert.False(updater.TakeApplyNow());
    }

    // ------------------------------------------------------------------ 画面

    [Fact]
    public void 一般設定の一番下にアップデートのまとまりがあり_ダッシュボードには出さない()
    {
        using var view = SampleWindow.Create([]);
        view.Resize(new Size(1060, 2000), 1f);
        view.SelectTab(DesktopTab.Startup);

        var apply = view.TargetRect(SettingsView.HitKind.ApplyUpdate)!.Value;
        Assert.True(apply.Y > view.TargetRect(SettingsView.HitKind.CopyCommand)!.Value.Bottom);
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.CheckUpdates));
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.UpdateCheck));

        using var dashboard = new SettingsDashboardView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });
        Assert.Null(dashboard.TargetRect(SettingsView.HitKind.ApplyUpdate));
        Assert.Null(dashboard.TargetRect(SettingsView.HitKind.UpdateCheck));
    }

    [Fact]
    public void インストーラーで入れた版でなければボタンは押せず理由を出す()
    {
        var commands = new List<DesktopCommand>();
        using var view = OpenUpdateSection(commands, UpdateStatus.Unavailable);

        var check = view.TargetRect(SettingsView.HitKind.CheckUpdates)!.Value;
        SampleWindow.Click(view, check);
        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.ApplyUpdate)!.Value);
        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.UpdateCheck)!.Value);

        Assert.Empty(commands);
        Assert.False(view.TakeUpdateRequest());

        view.MouseMove(new PointF(check.X + (check.Width / 2f), check.Y + (check.Height / 2f)));
        Assert.Equal("自動アップデートを利用するにはインストーラー版をインストールしてください", view.SettingsHint);
    }

    [Fact]
    public void 今すぐ確認は命令を送り_更新して再起動は確かめる画面を頼むだけ()
    {
        var commands = new List<DesktopCommand>();
        using var view = OpenUpdateSection(commands, new UpdateStatus(UpdatePhase.UpToDate, CheckedAtUtc: Start));

        // 新しい版がなければ「更新して再起動」は押せない。
        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.ApplyUpdate)!.Value);
        Assert.False(view.TakeUpdateRequest());

        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.CheckUpdates)!.Value);
        Assert.IsType<DesktopCommand.CheckForUpdates>(Assert.Single(commands));

        view.SetSettings(view.Settings with { Update = new UpdateStatus(UpdatePhase.Available, "0.2.0") });
        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.ApplyUpdate)!.Value);

        Assert.Single(commands); // ApplyUpdate はウィンドウが「はい」を確かめてから送る
        Assert.True(view.TakeUpdateRequest());
        Assert.False(view.TakeUpdateRequest());
    }

    [Fact]
    public void 自動で確認する切り替えは設定として送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = OpenUpdateSection(commands, new UpdateStatus(UpdatePhase.UpToDate));

        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.UpdateCheck)!.Value);

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.False(change.Settings.UpdateCheckEnabled);
        Assert.Equal(SettingsField.UpdateCheck, change.Fields);
    }

    [Fact]
    public void 新しい版があれば状態の段の右端にリンクを出し_押すと確かめる画面を頼む()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);

        Assert.Null(view.UpdateLinkText);
        Assert.True(view.UpdateLinkRect.IsEmpty);

        view.SetSettings(view.Settings with { Update = new UpdateStatus(UpdatePhase.Available, "0.2.0") });
        view.SetStatus(DesktopStatus.Initial with { UpdateVersion = "0.2.0" });

        Assert.Equal("アプリを更新", view.UpdateLinkText);
        var link = view.UpdateLinkRect;
        Assert.True(link.Right < view.CreditLinkRect.X);
        Assert.Equal(view.CreditLinkRect.Y, link.Y);

        SampleWindow.Click(view, link);
        Assert.Empty(commands);
        Assert.True(view.TakeUpdateRequest());

        // 落としている間は進み具合を出し、押せない。
        view.SetStatus(DesktopStatus.Initial with { UpdateVersion = "0.2.0", UpdateProgress = 42 });
        Assert.Equal("v0.2.0 をダウンロード中 42%", view.UpdateLinkText);

        SampleWindow.Click(view, view.UpdateLinkRect);
        Assert.False(view.TakeUpdateRequest());
    }

    [Fact]
    public void まとまりの1行目にいまの版と状態を出す()
    {
        Assert.Equal("自動アップデートは利用できません", SettingsView.UpdateStatusText(UpdateStatus.Unavailable));
        Assert.Equal(string.Empty, SettingsView.UpdateStatusText(new UpdateStatus(UpdatePhase.Idle)));
        Assert.Equal("最新のバージョンです", SettingsView.UpdateStatusText(new UpdateStatus(UpdatePhase.UpToDate, CheckedAtUtc: Start)));
        Assert.Equal("ログの読み込み完了を待っています…", SettingsView.UpdateStatusText(new UpdateStatus(UpdatePhase.Waiting, "0.2.0", 100)));

        var failed = SettingsView.UpdateStatusText(new UpdateStatus(UpdatePhase.Failed, CheckedAtUtc: Start, Error: "x"));
        var local = Start.ToLocalTime().ToString("MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"バージョン確認に失敗しました ({local})", failed);
        Assert.Contains("新バージョン v0.2.0 が公開されています", SettingsView.UpdateStatusText(new UpdateStatus(UpdatePhase.Available, "0.2.0")));
        Assert.Contains("42%", SettingsView.UpdateStatusText(new UpdateStatus(UpdatePhase.Downloading, "0.2.0", 42)));
        Assert.Equal($"現在のバージョン: {AppInfo.DisplayVersion}", SettingsView.UpdateVersionText);
    }

    [Fact]
    public void 新バージョンがあれば手首のパネルの下部に知らせを描く()
    {
        using var renderer = new PanelRenderer(new PanelStyle());
        renderer.RenderRows([]);

        renderer.Compose(0f);
        var plain = renderer.GetPixels().ToArray();

        renderer.Compose(0f, new PanelDecorations { UpdateAvailable = true });
        var notice = renderer.GetPixels().ToArray();

        // 違うのは下部の段だけ（アプリ名と版の左）。
        var footerTop = renderer.Height - new PanelStyle().FooterHeight;
        var differs = Enumerable.Range(0, plain.Length).Where(i => plain[i] != notice[i]).ToList();

        Assert.NotEmpty(differs);
        Assert.All(differs, i => Assert.True(i / 4 / renderer.Width >= footerTop));
    }

    [Fact]
    public void 確かめる画面の文と既定のボタン()
    {
        Assert.Equal(
            "新バージョン v0.2.0 へ更新します。\n更新が完了するとアプリは自動で再起動します。\n設定や訪問履歴は引き継がれます。\n更新を開始しますか？",
            UpdateConfirm.Text("0.2.0"));

        // 既定のボタンは「いいえ」（Enter の押し間違いで更新しない）。
        Assert.Equal(NativeMethods.MB_DEFBUTTON2, UpdateConfirm.Flags & NativeMethods.MB_DEFBUTTON2);
        Assert.Equal(NativeMethods.MB_YESNO, UpdateConfirm.Flags & NativeMethods.MB_YESNO);
    }

    // ------------------------------------------------------------------ アンインストール

    [Fact]
    public void アンインストールではログオン時の起動の登録を別の場所を指す古いものも消す()
    {
        var registry = new FakeStartupRegistry();
        registry.Run[StartupRegistration.ValueName] = @"""C:\Old\VRCInstanceWristory.exe"" --minimized";
        registry.Approved[StartupRegistration.ValueName] = [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var startup = new StartupRegistration(registry, @"C:\Tools\VRCInstanceWristory\VRCInstanceWristory.exe");

        Assert.True(startup.Remove());
        Assert.Empty(registry.Run);
        Assert.Empty(registry.Approved);

        // 登録がなくても成功。
        Assert.True(startup.Remove());
    }

    [Fact]
    public void アンインストールでは保存先のフォルダーを中身ごと消す()
    {
        using var root = new TempDirectory("uninstall");
        var data = Path.Combine(root.Path, AppInfo.InternalName);
        Directory.CreateDirectory(Path.Combine(data, "thumbnails"));
        File.WriteAllText(Path.Combine(data, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(data, "thumbnails", "a.jpg"), "x");

        Assert.True(VRCInstanceWristory.Modes.UninstallHook.DeleteData(data));
        Assert.False(Directory.Exists(data));

        // もうなければ、消えている扱い。
        Assert.True(VRCInstanceWristory.Modes.UninstallHook.DeleteData(data));
    }

    [Fact]
    public void アンインストールでもこのアプリの名前でないフォルダーは消さない()
    {
        using var root = new TempDirectory("uninstall");
        var other = Path.Combine(root.Path, "Documents");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "keep.txt"), "x");

        Assert.False(VRCInstanceWristory.Modes.UninstallHook.DeleteData(other));
        Assert.True(File.Exists(Path.Combine(other, "keep.txt")));
    }

    [Fact]
    public void インストール先は保存先と別の名前にする()
    {
        // Velopack はインストールし直す・アンインストールするときにインストール先をまるごと消す。同じ名前だと設定と訪問履歴も消える。
        Assert.NotEqual(AppInfo.InternalName, AppInfo.PackageId, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void インストーラーを作るスクリプトはアプリと同じpackIdとリポジトリを使う()
    {
        var script = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "scripts", "package.ps1"));

        Assert.Contains($"$packId = '{AppInfo.PackageId}'", script);
        Assert.Contains($"[string] $RepoUrl = '{AppInfo.RepositoryUrl}'", script);

        // Velopack の版は、アプリが使うライブラリと vpk（インストーラーを作る道具）で揃える。
        var project = File.ReadAllText(Path.Combine(TestPaths.AppProject, "VRCInstanceWristory.csproj"));
        var tools = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "dotnet-tools.json"));
        var version = System.Text.RegularExpressions.Regex.Match(project, "Include=\"Velopack\" Version=\"([^\"]+)\"").Groups[1].Value;

        Assert.NotEmpty(version);
        Assert.Contains($"\"version\": \"{version}\"", tools);
    }

    // ------------------------------------------------------------------ 道具

    private static DesktopView OpenUpdateSection(List<DesktopCommand> commands, UpdateStatus status)
    {
        var view = SampleWindow.Create(commands);
        view.Resize(new Size(1060, 2000), 1f);
        view.SelectTab(DesktopTab.Startup);
        view.SetSettings(view.Settings with { Update = status });
        return view;
    }

    private static void WaitFor(Func<bool> condition)
    {
        var limit = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (!condition())
        {
            if (DateTime.UtcNow > limit)
                throw new TimeoutException("条件が5秒以内に成り立ちませんでした。");

            Thread.Sleep(5);
        }
    }

    /// <summary>更新の仕組みの代わり。確かめた結果と、ダウンロードの進み・終わりを検証から決める。</summary>
    private sealed class FakeUpdateBackend : IUpdateBackend
    {
        private TaskCompletionSource? _download;
        private Action<int>? _progress;

        public bool IsInstalled { get; init; } = true;

        public string? NextVersion { get; init; }

        public Exception? CheckError { get; init; }

        public int CheckCount;

        public int DownloadCount;

        public int ApplyCount;

        public bool DownloadStarted => Volatile.Read(ref _download) is not null;

        public Task<string?> CheckAsync(CancellationToken cancel)
        {
            Interlocked.Increment(ref CheckCount);
            return CheckError is { } error ? Task.FromException<string?>(error) : Task.FromResult(NextVersion);
        }

        public Task DownloadAsync(Action<int> progress, CancellationToken cancel)
        {
            Interlocked.Increment(ref DownloadCount);
            _progress = progress;
            var download = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _download, download);
            return download.Task;
        }

        public void ApplyAfterExit() => ApplyCount++;

        public void ReportProgress(int percent) => _progress!(percent);

        public void FinishDownload() => _download!.SetResult();

        public void FailDownload(Exception error) => _download!.SetException(error);
    }
}
