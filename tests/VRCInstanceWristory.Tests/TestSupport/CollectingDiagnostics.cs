using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Tests;

/// <summary>検証用に診断を貯める。各行は「INFO 」「NOTE 」「WARN 」「ERROR 」で始まる。</summary>
public sealed class CollectingDiagnostics : IDiagnostics
{
    public List<string> Messages { get; } = [];

    public void Info(string message) => Messages.Add("INFO " + message);

    public void Notice(string message) => Messages.Add("NOTE " + message);

    public void Warn(string message) => Messages.Add("WARN " + message);

    public void Error(string message) => Messages.Add("ERROR " + message);
}
