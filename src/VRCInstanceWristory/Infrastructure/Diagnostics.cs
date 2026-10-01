namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// PC側の診断出力。VR内へは転送しない（仕様8.1節）。
///
/// | 段階 | 使いどころ | <c>--verbose</c> なし |
/// | --- | --- | --- |
/// | <see cref="Info"/> | 追跡用の細かな記録 | 出さない |
/// | <see cref="Notice"/> | 正常なできごとのうち、残しておきたいもの（設定の保存・SteamVRへの接続など） | 出す |
/// | <see cref="Warn"/> | 思いどおりでないが続けられること | 出す |
/// | <see cref="Error"/> | 失敗 | 出す |
/// </summary>
public interface IDiagnostics
{
    void Info(string message);

    /// <summary>正常なできごとで、<c>--verbose</c> を付けなくても残すもの。警告ではない。既定では <see cref="Info"/> と同じ扱い。</summary>
    void Notice(string message) => Info(message);

    void Warn(string message);

    void Error(string message);
}

public sealed class ConsoleDiagnostics(bool verbose = false) : IDiagnostics
{
    private readonly Lock _gate = new();

    public bool Verbose { get; set; } = verbose;

    public void Info(string message)
    {
        if (Verbose)
            Write("INFO ", message);
    }

    public void Notice(string message) => Write("NOTE ", message);

    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {level} {message}");
        }
    }
}

public sealed class NullDiagnostics : IDiagnostics
{
    public static readonly NullDiagnostics Instance = new();

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message)
    {
    }
}
