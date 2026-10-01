using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Tests;

/// <summary>ログオン時の起動の登録先（<c>HKCU\...\Run</c> と StartupApproved）の代わり。メモリの中だけに書く。</summary>
public sealed class FakeStartupRegistry : IStartupRegistry
{
    public Dictionary<string, string> Run { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Approved { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetRunValue(string name) => Run.GetValueOrDefault(name);

    public void SetRunValue(string name, string command) => Run[name] = command;

    public void DeleteRunValue(string name) => Run.Remove(name);

    public byte[]? GetApprovedValue(string name) => Approved.GetValueOrDefault(name);

    public void DeleteApprovedValue(string name) => Approved.Remove(name);
}
