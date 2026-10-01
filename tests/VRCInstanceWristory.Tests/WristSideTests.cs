using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 右手首への切り替え（2026-09-26のユーザー指定→実装メモ5.49）。
///
/// パネルを右コントローラーに付け、レイ・スティック・グリップ・トリガーは左手で読む。
/// 右手首の配置は、書いていなければ左手首の配置を左右反転したもの。右手首で置き直すと右の項目へ保存する。
/// 実際に左右を入れ替えて使えるかは実機で確かめる必要がある（ここでは計算と設定と同梱の割り当てを確かめる）。
/// </summary>
public class WristSideTests
{
    [Fact]
    public void 左右を反転しても回転のままで_もう一度反転すると元に戻る()
    {
        var rotation = Rotations.FromEulerDegrees(-62f, 12f, -7f);
        var mirrored = WristSides.Mirror(rotation);

        // 鏡像ではなく回転（長さ1）。2回反転すれば元どおり。
        Assert.Equal(1f, mirrored.Length(), 4);
        var twice = WristSides.Mirror(mirrored);
        Assert.True(MathF.Abs(Quaternion.Dot(twice, rotation)) > 0.9999f);

        // 度数では pitch はそのまま、yaw と roll の符号が反転する。
        var (pitch, yaw, roll) = Rotations.ToEulerDegrees(mirrored);
        Assert.Equal(-62f, pitch, 2);
        Assert.Equal(-12f, yaw, 2);
        Assert.Equal(7f, roll, 2);

        Assert.Equal(new Vector3(-0.01f, 0.02f, 0.09f), WristSides.Mirror(new Vector3(0.01f, 0.02f, 0.09f)));
    }

    [Fact]
    public void 反転した配置では左右の向きだけが鏡映しになり_文字は裏返らない()
    {
        var rotation = Rotations.FromEulerDegrees(-70f, 20f, 10f);
        var mirrored = WristSides.Mirror(rotation);
        var m = new Vector3(-1f, 1f, 1f);

        // パネルの上（+Y）と表（+Z）は、左で置いたときの向きを左右反転したもの。
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitY, mirrored), Vector3.Transform(Vector3.UnitY, rotation) * m) < 1e-4f);
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, mirrored), Vector3.Transform(Vector3.UnitZ, rotation) * m) < 1e-4f);

        // 右（+X）は反転の反対向き。上と表から右手系で決まるので、文字は左から右へ読める向きのまま。
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitX, mirrored), -(Vector3.Transform(Vector3.UnitX, rotation) * m)) < 1e-4f);
    }

    [Fact]
    public void 右手首の配置は書いていなければ左手首を左右反転したもの()
    {
        var settings = new AppSettings
        {
            TranslationMeters = [0.01f, 0.02f, 0.09f],
            RotationEulerDegrees = [-70f, 15f, 5f],
        };

        var (translation, rotation) = settings.PlacementFor(WristSide.Right);
        Assert.Equal(new Vector3(-0.01f, 0.02f, 0.09f), translation);

        var (pitch, yaw, roll) = Rotations.ToEulerDegrees(rotation);
        Assert.Equal(-70f, pitch, 2);
        Assert.Equal(-15f, yaw, 2);
        Assert.Equal(-5f, roll, 2);

        // 左手首は今までどおり。
        Assert.Equal(settings.Translation, settings.PlacementFor(WristSide.Left).Translation);
    }

    [Fact]
    public void 右手首で置き直すと右の項目へだけ保存し_左手首の配置には触れない()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        File.WriteAllText(path, """
            {
              "translationMeters": [0.0, 0.02, 0.09],
              "rotationEulerDegrees": [-70.0, 0.0, 0.0],
              "rotationQuaternion": [0.0, 0.0, 0.0, 1.0]
            }
            """);

        var settings = AppSettings.Load(path, new CollectingDiagnostics());
        settings.SetPlacement(WristSide.Right, new Vector3(-0.02f, 0.03f, 0.1f), Rotations.FromEulerDegrees(-60f, -10f, 0f));
        settings.SaveFields(path, AppSettings.PlacementField(WristSide.Right));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        // 左の項目（上級者向けのクォータニオン指定も）はそのまま。
        Assert.NotNull(root["rotationQuaternion"]);
        Assert.Equal([0.0f, 0.02f, 0.09f], root["translationMeters"]!.AsArray().Select(n => (float)n!));

        var reloaded = AppSettings.Load(path, new CollectingDiagnostics());
        Assert.Equal(new Vector3(-0.02f, 0.03f, 0.1f), reloaded.PlacementFor(WristSide.Right).Translation);
        Assert.Equal(-60f, reloaded.RightRotationEulerDegrees![0], 2);

        // 右手首の「既定に戻す」は、右の項目を消して左の反転へ戻す。
        reloaded.ResetPlacement(WristSide.Right);
        Assert.Null(reloaded.RightTranslationMeters);
        Assert.Equal(WristSides.Mirror(reloaded.Translation), reloaded.PlacementFor(WristSide.Right).Translation);
    }

    [Fact]
    public void 手首の設定の既定は左_知らない値と壊れた右の配置は知らせて戻す()
    {
        Assert.Equal(WristSide.Left, new AppSettings().Wrist);

        var log = new CollectingDiagnostics();
        var settings = new AppSettings
        {
            WristSide = "middle",
            RightTranslationMeters = [0f, float.NaN, 0f],
            RightRotationEulerDegrees = [0f, 0f],
        };

        settings.Validate(log);

        Assert.Equal("left", settings.WristSide);
        Assert.Null(settings.RightTranslationMeters);
        Assert.Null(settings.RightRotationEulerDegrees);
        Assert.Contains(log.Messages, m => m.Contains("wristSide"));
        Assert.Contains(log.Messages, m => m.Contains("rightTranslationMeters"));
        Assert.Contains(log.Messages, m => m.Contains("rightRotationEulerDegrees"));

        var right = new AppSettings { WristSide = "RIGHT" };
        right.Validate(new CollectingDiagnostics());
        Assert.Equal(WristSide.Right, right.Wrist);
        Assert.Equal("right", right.WristSide);
    }

    [Fact]
    public void 設定の画面で右手首を選ぶと手首の変更を送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SelectTab(DesktopTab.Panel);

        var right = view.TargetRect(SettingsView.HitKind.WristSide, 1)!.Value;
        view.MouseMove(SampleWindow.Center(right));
        view.MouseDown(SampleWindow.Center(right));
        view.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(WristSide.Right, change.Settings.Wrist);
        Assert.Equal(SettingsField.WristSide, change.Fields);

        // いま選んでいる手首をもう一度押しても何も送らない。
        view.MouseDown(SampleWindow.Center(right));
        view.MouseUp();
        Assert.Single(commands);

        // 設定へ写すと手首が変わる。
        var settings = new AppSettings();
        change.Settings.ApplyTo(settings, change.Fields);
        Assert.Equal(WristSide.Right, settings.Wrist);
    }

    [Fact]
    public void パネルの付け替えでその手首の配置を取り直す()
    {
        // OpenVR は初期化しない（配置の値だけを確かめる）。
        var settings = new AppSettings { TranslationMeters = [0.01f, 0.02f, 0.09f] };
        using var renderer = new PanelRenderer(new PanelStyle());
        using var session = new SteamVrSession(NullDiagnostics.Instance);
        var input = new SteamVrInput(NullDiagnostics.Instance);
        var controller = new OverlayController(session, input, settings, renderer, new Core.Scrolling.ScrollController(), NullDiagnostics.Instance);

        Assert.Equal(WristSide.Left, controller.Wrist);
        Assert.Equal(WristSide.Right, input.OperatingHand);

        controller.SetWristSide(WristSide.Right);

        Assert.Equal(WristSide.Right, controller.Wrist);
        Assert.Equal(WristSide.Left, input.OperatingHand);
        Assert.Equal(new Vector3(-0.01f, 0.02f, 0.09f), controller.Translation);

        controller.SetWristSide(WristSide.Left);
        Assert.Equal(new Vector3(0.01f, 0.02f, 0.09f), controller.Translation);
    }

    // ------------------------------------------------------------------ 同梱の割り当て

    [Fact]
    public void 左手のaimの動作をaction_manifestに持つ()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.Resources, "actions.json")))!;
        var names = manifest["actions"]!.AsArray().Select(a => (string?)a!["name"]).ToList();

        Assert.Contains(SteamVrInput.LeftAimAction, names);
        Assert.Contains(SteamVrInput.RightAimAction, names);

        foreach (var localization in manifest["localization"]!.AsArray())
            Assert.NotNull(localization![SteamVrInput.LeftAimAction]);
    }

    /// <summary>コードが読む動作（<see cref="SteamVrInput"/> の名前）。action manifest にはこれだけを置く（読まない動作を残さない）。</summary>
    private static readonly string[] ReadActions =
    [
        SteamVrInput.RightAimAction,
        SteamVrInput.LeftAimAction,
        SteamVrInput.ScrollAction,
        SteamVrInput.GrabAction,
        SteamVrInput.CloseAction,
        SteamVrInput.ClickAction,
        SteamVrInput.HapticAction,
    ];

    private static List<string> ManifestActions()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.Resources, "actions.json")))!;
        return manifest["actions"]!.AsArray().Select(a => (string)a!["name"]!).ToList();
    }

    [Fact]
    public void action_manifestの動作はコードが読むものだけ()
    {
        Assert.Equal(ReadActions.Order(StringComparer.Ordinal), ManifestActions().Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(BindingFiles))]
    public void action_manifestの動作はすべて同梱の割り当てに入っている(string file)
    {
        var main = JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.Resources, "bindings", file)))!["bindings"]!["/actions/main"]!;

        var outputs = main["poses"]!.AsArray().Select(p => (string)p!["output"]!)
            .Concat(main["haptics"]!.AsArray().Select(h => (string)h!["output"]!))
            .Concat(main["sources"]!.AsArray().SelectMany(s => s!["inputs"]!.AsObject().Select(i => (string)i.Value!["output"]!)))
            .ToHashSet(StringComparer.Ordinal);

        var actions = ManifestActions().ToHashSet(StringComparer.Ordinal);

        // manifest の動作はどれも割り当ててあり、割り当ての側にも manifest にない動作を残さない。
        Assert.Subset(outputs, actions);
        Assert.Subset(actions, outputs);
    }

    public static TheoryData<string> BindingFiles()
    {
        var data = new TheoryData<string>();

        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.Resources, "bindings"), "*.json"))
            data.Add(Path.GetFileName(file));

        return data;
    }

    [Theory]
    [MemberData(nameof(BindingFiles))]
    public void 同梱の割り当ては左右どちらの手でも指して_スクロールして_掴んで_押せる(string file)
    {
        var binding = JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.Resources, "bindings", file)))!;
        var main = binding["bindings"]!["/actions/main"]!;

        var poses = main["poses"]!.AsArray().Select(p => ((string?)p!["output"], (string?)p["path"])).ToList();
        Assert.Contains((SteamVrInput.RightAimAction, "/user/hand/right/pose/tip"), poses);
        Assert.Contains((SteamVrInput.LeftAimAction, "/user/hand/left/pose/tip"), poses);

        var sources = main["sources"]!.AsArray()
            .SelectMany(s => s!["inputs"]!.AsObject().Select(i => ((string?)s["path"], (string?)i.Value!["output"])))
            .ToList();

        foreach (var action in new[] { SteamVrInput.ScrollAction, SteamVrInput.GrabAction, SteamVrInput.ClickAction })
        {
            Assert.Contains(sources, s => s.Item2 == action && s.Item1!.StartsWith("/user/hand/right/", StringComparison.Ordinal));
            Assert.Contains(sources, s => s.Item2 == action && s.Item1!.StartsWith("/user/hand/left/", StringComparison.Ordinal));
        }

        // JSON として正しい（SteamVR が読める）。
        Assert.NotNull(JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.Resources, "bindings", file))));
    }
}
