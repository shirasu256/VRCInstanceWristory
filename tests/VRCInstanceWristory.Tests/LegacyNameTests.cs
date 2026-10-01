using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 旧名（InstanceIDLogger）からの引き継ぎ（→実装メモ5.84）。ログオン時の起動の登録と保存先を新しい名前へ移し、
/// 旧版と新版で Mutex・パイプ・SteamVRのキーが重ならないようにする。
/// レジストリは差し替えた入れ物（<see cref="FakeStartupRegistry"/>）で、保存先は一時フォルダーで確かめる。
/// </summary>
public class LegacyNameTests
{
    private const string Exe = @"C:\Apps\VRC Instance Wristory\VRCInstanceWristory.exe";

    [Fact]
    public void 旧名の有効な登録はこの実行ファイルで登録し直して旧名の値を消す()
    {
        var registry = new FakeStartupRegistry();
        registry.Run[LegacyName.Name] = "\"D:\\dist\\InstanceIDLogger\\InstanceIDLogger.exe\" --minimized";
        registry.Approved[LegacyName.Name] = [0x02];

        var startup = new StartupRegistration(registry, Exe);
        Assert.True(startup.MigrateLegacy());

        Assert.True(startup.IsEnabled);
        Assert.Equal($"\"{Exe}\" --minimized", registry.Run[StartupRegistration.ValueName]);
        Assert.False(registry.Run.ContainsKey(LegacyName.Name));
        Assert.False(registry.Approved.ContainsKey(LegacyName.Name));

        // 2回目は引き継ぐものがない。
        Assert.False(startup.MigrateLegacy());
    }

    [Fact]
    public void 旧名の登録が無効にされていたら消すだけで登録し直さない()
    {
        var registry = new FakeStartupRegistry();
        registry.Run[LegacyName.Name] = "\"D:\\old\\InstanceIDLogger.exe\" --minimized";
        registry.Approved[LegacyName.Name] = [0x03];

        var startup = new StartupRegistration(registry, Exe);
        Assert.True(startup.MigrateLegacy());

        Assert.False(startup.IsEnabled);
        Assert.Empty(registry.Run);
        Assert.Empty(registry.Approved);
    }

    [Fact]
    public void 旧名の保存先は新しい保存先がまだなければフォルダーごと移す()
    {
        using var tempDirectory = new TempDirectory("legacy");
        var root = tempDirectory.Path;
        var legacy = Path.Combine(root, LegacyName.Name);
        var current = Path.Combine(root, "VRCInstanceWristory");

        Directory.CreateDirectory(Path.Combine(legacy, "thumbnails"));
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");

        Assert.NotNull(LegacyName.MoveDataDirectory(legacy, current));
        Assert.True(File.Exists(Path.Combine(current, "settings.json")));
        Assert.True(Directory.Exists(Path.Combine(current, "thumbnails")));
        Assert.False(Directory.Exists(legacy));

        // 新しい保存先がすでにあれば、そちらを正として触らない。
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"old\":true}");
        Assert.Null(LegacyName.MoveDataDirectory(legacy, current));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(current, "settings.json")));

        // 旧い方がなければ何もしない。
        Assert.Null(LegacyName.MoveDataDirectory(Path.Combine(root, "none"), Path.Combine(root, "other")));
        Assert.False(Directory.Exists(Path.Combine(root, "other")));
    }

    [Fact]
    public void 新しい名前は旧名と重ならない()
    {
        // 旧版が起動中でも Mutex・パイプ・SteamVRのキーが食い違わず、どちらの版かを見分けられる。
        Assert.NotEqual(LegacyName.MutexName, SingleInstance.MutexName);
        Assert.NotEqual(LegacyName.SteamVrApplicationKey, SteamVrSession.ApplicationKey);
        Assert.NotEqual(LegacyName.Name, StartupRegistration.ValueName);
        Assert.Equal("VRC Instance Wristory", AppInfo.DisplayName);
        Assert.Equal("VRCInstanceWristory", typeof(AppInfo).Assembly.GetName().Name);
    }
}
