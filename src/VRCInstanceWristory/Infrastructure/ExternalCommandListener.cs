using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>外部から受け取ったコマンド。</summary>
public enum ExternalCommand
{
    /// <summary>訪問履歴をリセットする（手首のパネルの「リセット」と同じ）。</summary>
    ResetHistory,
}

/// <summary>
/// 外部からのコマンドの受け口（2026-09-28のユーザー指定→実装メモ5.83）。
///
/// 名前付きパイプ <c>\\.\pipe\VRCInstanceWristory</c> を開いて待ち、書き込まれた1行を読む。
/// OyasumiVR の「コマンドの実行」は、書いた内容を一時的な <c>.bat</c> にして cmd で実行するので、
/// cmd の <c>echo</c> を名前付きパイプへ向けるだけで届く（<see cref="CommandLine"/>）。このアプリの実行ファイルを
/// もう1つ起動しないので、実行ファイルの場所にも、ウィンドウを出しているかにも左右されない。
///
/// パイプは同じユーザーだけが開けるようにし、ネットワーク越しには開けないようにする（名前付きパイプは、既定では別のPCからも開ける作りのため）。
/// 受け取ったコマンドは待ち行列に積むだけで、履歴を消すのは主ループ（<see cref="TryTake"/>）。
/// アプリが動いていないとき・受け付けをオフにしているときは、パイプがないので cmd は「指定されたファイルが見つかりません。」で終わる。
/// </summary>
public sealed class ExternalCommandListener : IDisposable
{
    /// <summary>パイプの名前（<c>\\.\pipe\</c> の後ろ）。検証では別の名前を使う（本物の名前は起動中のアプリが持っている）。</summary>
    public const string DefaultPipeName = AppInfo.InternalName;

    /// <summary>訪問履歴をリセットさせる語。</summary>
    public const string ResetHistoryWord = "reset";

    /// <summary>1回に読む上限。コマンドは短い語だけなので、長いものは読まずに捨てる。</summary>
    private const int MaxMessageBytes = 256;

    /// <summary>つないだ相手が書き終えるまで待つ上限。</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    /// <summary>パイプを作れなかったとき（同じ名前を別のユーザーのアプリが持っているなど）に作り直すまでの間。</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly string _pipeName;
    private readonly IDiagnostics _log;
    private readonly ConcurrentQueue<ExternalCommand> _received = new();

    private CancellationTokenSource? _stop;
    private Task? _loop;

    public ExternalCommandListener(string pipeName, IDiagnostics log)
    {
        _pipeName = pipeName;
        _log = log;
    }

    /// <summary>
    /// 外部のアプリに書いてもらうコマンド（cmd の1行）。<c>&gt;</c> の前に空白を入れない
    /// （入れると、その空白まで書き込まれる）。
    /// </summary>
    public static string CommandLine(string pipeName = DefaultPipeName) => $@"echo {ResetHistoryWord}>\\.\pipe\{pipeName}";

    /// <summary>止めるときに、受け口のループが終わるのを待つ上限。主ループ（VRの描画）を長く止めないよう短くする。</summary>
    private static readonly TimeSpan StopWait = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 受け付けているか（パイプを開いて待っているか）。受け口が思わぬ例外で止まったときも false になる
    /// （そのときは <see cref="Start"/> で始め直せる）。
    /// </summary>
    public bool Listening => _loop is { IsCompleted: false };

    /// <summary>受け付けを始める・やめる（設定の「外部からの履歴リセットコマンドを受け付ける」）。</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled)
            Start();
        else
            Stop();
    }

    public void Start()
    {
        if (Listening)
            return;

        // 思わぬ例外で止まっていたら、その後始末をしてから始め直す。
        Stop();

        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        _loop = Task.Run(() => RunAsync(token), token);
        _log.Info($@"外部からのコマンドを受け付けます: \\.\pipe\{_pipeName}");
    }

    public void Stop()
    {
        if (_loop is not { } loop || _stop is not { } stop)
            return;

        _loop = null;
        _stop = null;

        var wasListening = !loop.IsCompleted;
        stop.Cancel();

        try
        {
            loop.Wait(StopWait);
        }
        catch (AggregateException)
        {
            // 止めたときの取り消しの例外。
        }

        // 取り消しの印は、ループが使い終わってから捨てる（待ちきれなかったときは、終わったところで捨てる）。
        if (loop.IsCompleted)
            stop.Dispose();
        else
            loop.ContinueWith(_ => stop.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

        if (wasListening)
            _log.Info("外部からのコマンドの受け付けをやめました。");
    }

    /// <summary>受け取ったコマンドを1つ取り出す（主ループから呼ぶ）。</summary>
    public bool TryTake(out ExternalCommand command) => _received.TryDequeue(out command);

    /// <summary>受け取った内容をコマンドへ直す。知らない語なら null。前後の空白・改行と大文字小文字は問わない。</summary>
    public static ExternalCommand? Parse(string text)
        => string.Equals(text.Trim(), ResetHistoryWord, StringComparison.OrdinalIgnoreCase) ? ExternalCommand.ResetHistory : null;

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await ListenAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 止めた。
        }
        catch (Exception ex)
        {
            // 思わぬ例外で受け口が止まった。黙って止まると、設定はオンのままコマンドが届かなくなるので、PC側へ知らせる。
            _log.Error($"外部からのコマンドの受け口が止まりました（設定を切り替えると始め直します）: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        var warned = false;

        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream server;

            try
            {
                server = CreateServer(_pipeName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 同じ名前のパイプを別のプロセス（別のユーザーで動いているこのアプリなど）が持っている。
                if (!warned)
                    _log.Warn($"外部からのコマンドの受け口を開けません（{RetryDelay.TotalSeconds:0}秒ごとに試し直します）: {ex.Message}");

                warned = true;

                try
                {
                    await Task.Delay(RetryDelay, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            warned = false;

            await using (server)
            {
                try
                {
                    await server.WaitForConnectionAsync(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException ex)
                {
                    _log.Info($"外部からの接続を受けられません: {ex.Message}");
                    continue;
                }

                var text = await ReadMessageAsync(server, token);

                if (text is null)
                    continue;

                if (Parse(text) is { } command)
                {
                    _received.Enqueue(command);
                    _log.Info($"外部からのコマンドを受け取りました: {command}");
                }
                else
                {
                    _log.Info($"外部から知らないコマンドが届きました（無視します）: {Printable(text)}");
                }
            }
        }
    }

    /// <summary>相手が閉じるまで（上限まで）読む。読めなければ null。</summary>
    private static async Task<string?> ReadMessageAsync(NamedPipeServerStream server, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(ReadTimeout);

        var buffer = new byte[MaxMessageBytes];
        var length = 0;

        try
        {
            while (length < buffer.Length)
            {
                var read = await server.ReadAsync(buffer.AsMemory(length), timeout.Token);

                if (read == 0)
                    break;

                length += read;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            // 相手が書いてすぐ閉じたときに、読み終わる前に切れたと出ることがある。読めたところまでを使う。
        }

        // cmd の echo はコンソールのコードページで書くが、使う語は ASCII だけなので問題にならない。
        return length == 0 ? null : Encoding.ASCII.GetString(buffer, 0, length);
    }

    private static string Printable(string text)
    {
        var trimmed = text.Trim();
        var shown = trimmed.Length > 40 ? trimmed[..40] + "…" : trimmed;
        return new string([.. shown.Select(c => char.IsControl(c) ? '?' : c)]);
    }

    /// <summary>
    /// 同じユーザーだけが書き込め、ネットワーク越しには開けないパイプを作る。
    /// 同じ名前のパイプが既にあれば作らない（1つだけと指定すると、最初の1つでなければ失敗する）。
    /// </summary>
    private static NamedPipeServerStream CreateServer(string pipeName)
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("ユーザーの SID が分かりません。");

        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    public void Dispose() => Stop();
}

/// <summary>
/// 外部からのコマンドで最後に訪問履歴をリセットした時刻の保存（→実装メモ5.83）。
/// 設定ではない記録なので settings.json には書かず、<see cref="AppPaths.ExternalReset"/> に残す。
/// </summary>
public static class ExternalResetRecord
{
    private sealed record Document(DateTime? LastRunUtc);

    private static readonly System.Text.Json.JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>読めなければ（まだない・壊れている）null。</summary>
    public static DateTime? Load(string path, IDiagnostics? log = null)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var document = System.Text.Json.JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Json);
            return document?.LastRunUtc is { } utc ? DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            log?.Warn($"外部からの履歴リセットの記録を読めません（なかったものとして続けます）: {ex.Message}");
            return null;
        }
    }

    public static void Save(string path, DateTime lastRunUtc, IDiagnostics? log = null)
    {
        try
        {
            // 書いている途中で電源が落ちても、途中で切れた記録を残さない（→実装メモ5.59）。
            AtomicFile.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new Document(DateTime.SpecifyKind(lastRunUtc, DateTimeKind.Utc)), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Error($"外部からの履歴リセットの記録を保存できません: {ex.Message}");
        }
    }
}
