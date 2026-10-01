using System.Numerics;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Tests;

/// <summary>仕様10節（設定）。無効な値は項目名を知らせて既定値へ戻す。</summary>
public class SettingsTests
{
    [Fact]
    public void 同梱の設定例は警告なしで読める()
    {
        var log = new CollectingDiagnostics();
        var settings = AppSettings.Load(Path.Combine(TestPaths.RepositoryRoot, "settings.example.json"), log);

        Assert.DoesNotContain(log.Messages, m => m.StartsWith("WARN") || m.StartsWith("ERROR"));
        Assert.Equal(0.14f, settings.OverlayWidthMeters);
        Assert.Equal(0.9f, settings.BackgroundOpacity);
        Assert.Equal(0.20f, settings.ScrollDeadzone);
        Assert.Equal(0.15f, settings.ViewAngleFadeSeconds);
        Assert.Equal(new AppSettings().CursorSizeMeters, settings.CursorSizeMeters);

        // 位置と角度は既定（実機で置いた配置を焼き込んだもの→実装メモ5.76）と同じ。
        Assert.Equal(new AppSettings().TranslationMeters, settings.TranslationMeters);
        Assert.Equal(new AppSettings().RotationEulerDegrees, settings.RotationEulerDegrees);

        // 角度は度数で書けて、そのままクォータニオンへ変換できる。
        Assert.Equal([-48.22f, -170.77f, -98.8f], settings.RotationEulerDegrees);
        Assert.Null(settings.RotationQuaternion);
        Assert.Equal(1f, settings.Rotation.Length(), 3);

        var (pitch, yaw, roll) = Rotations.ToEulerDegrees(settings.Rotation);
        Assert.Equal(-48.22f, pitch, 2);
        Assert.Equal(-170.77f, yaw, 2);
        Assert.Equal(-98.8f, roll, 2);
    }

    /// <summary>
    /// 右手で指した位置のカーソル（輪）の直径を半分にした（2026-09-25のユーザー指定→実装メモ5.36）。
    /// 輪はオーバーレイの幅の9割なので、0.012m（輪は約10.8mm）から0.006m（約5.4mm）へ下げた。
    /// </summary>
    [Fact]
    public void カーソルの既定は以前の半分の大きさ()
    {
        Assert.Equal(0.012f / 2f, new AppSettings().CursorSizeMeters);
    }

    [Fact]
    public void クォータニオン指定があればそちらを優先する()
    {
        var expected = Rotations.FromEulerDegrees(-30f, 12f, 5f);
        var settings = new AppSettings
        {
            RotationEulerDegrees = [-70f, 0f, 0f],
            RotationQuaternion = [expected.X, expected.Y, expected.Z, expected.W],
        };

        settings.Validate(new CollectingDiagnostics());

        Assert.Equal(expected.X, settings.Rotation.X, 4);
        Assert.Equal(expected.W, settings.Rotation.W, 4);
    }

    [Fact]
    public void 掴んで置いた配置は度数で書き戻される()
    {
        var settings = new AppSettings
        {
            RotationQuaternion = [0f, 0f, 0f, 1f],
        };

        settings.SetPlacement(new Vector3(0.01f, 0.03f, 0.11f), Rotations.FromEulerDegrees(-62.5f, 8f, -3f));

        Assert.Equal([0.01f, 0.03f, 0.11f], settings.TranslationMeters);
        Assert.Equal(-62.5f, settings.RotationEulerDegrees[0], 1);
        Assert.Equal(8f, settings.RotationEulerDegrees[1], 1);
        Assert.Equal(-3f, settings.RotationEulerDegrees[2], 1);

        // クォータニオンの直接指定は消えて、度数が使われる。
        Assert.Null(settings.RotationQuaternion);
    }

    [Fact]
    public void 無効な値は項目名を知らせて既定値へ戻す()
    {
        var log = new CollectingDiagnostics();
        var settings = new AppSettings
        {
            OverlayWidthMeters = -1f,
            BackgroundOpacity = 5f,
            ScrollDeadzone = 1.5f,
            TranslationMeters = [1f, float.NaN, 0f],
            RotationEulerDegrees = [400f, 0f, 0f],
            RotationQuaternion = [9f, 0f, 0f, 0f],
            ViewAngleFadeSeconds = 90f,
        };

        settings.Validate(log);

        Assert.Equal(0.14f, settings.OverlayWidthMeters);
        Assert.Equal(0.9f, settings.BackgroundOpacity);
        Assert.Equal(0.20f, settings.ScrollDeadzone);
        Assert.Equal([-0.1942504f, -0.024928987f, 0.08906134f], settings.TranslationMeters);
        Assert.Equal([-48.22f, -170.77f, -98.8f], settings.RotationEulerDegrees);
        Assert.Null(settings.RotationQuaternion);
        Assert.Equal(1f, settings.Rotation.Length(), 3);

        Assert.Contains(log.Messages, m => m.Contains("overlayWidthMeters"));
        Assert.Contains(log.Messages, m => m.Contains("translationMeters"));
        Assert.Contains(log.Messages, m => m.Contains("rotationEulerDegrees"));
        Assert.Contains(log.Messages, m => m.Contains("rotationQuaternion"));
        Assert.Equal(0.15f, settings.ViewAngleFadeSeconds);
        Assert.Contains(log.Messages, m => m.Contains("viewAngleFadeSeconds"));
    }

    [Fact]
    public void 設定ファイルがなければ既定値で動く()
    {
        var log = new CollectingDiagnostics();
        var settings = AppSettings.Load(Path.Combine(Path.GetTempPath(), $"vrciw-missing-{Guid.NewGuid():N}.json"), log);

        Assert.Equal(0.14f, settings.OverlayWidthMeters);
        Assert.DoesNotContain(log.Messages, m => m.StartsWith("ERROR"));
    }

    /// <summary>
    /// 掴んで置き直したときに全項目を書き出すと、その時点の値が固定されて
    /// コード側の既定値の変更が永久に反映されなくなる。位置だけを書き戻す。
    /// </summary>
    [Fact]
    public void 配置の保存は位置と角度以外の記述を残す()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        File.WriteAllText(path, """
            {
              "// 説明": "利用者が書いたコメント代わりのキー",
              "backgroundOpacity": 0.5,
              "translationMeters": [0.0, 0.02, 0.09],
              "rotationEulerDegrees": [-70.0, 0.0, 0.0],
              "rotationQuaternion": [0.0, 0.0, 0.0, 1.0],
              "overlayWidthMeters": 0.14,
              "idFontPixels": 60
            }
            """);

        var settings = AppSettings.Load(path, new CollectingDiagnostics());
        settings.SetPlacement(new Vector3(0.1f, 0.2f, 0.3f), Rotations.FromEulerDegrees(-45f, 10f, 0f));
        settings.OverlayWidthMeters = 0.99f;

        settings.SaveFields(path, SettingsField.Placement);

        var saved = AppSettings.Load(path, new CollectingDiagnostics());

        // 書き戻した項目。
        Assert.Equal([0.1f, 0.2f, 0.3f], saved.TranslationMeters);
        Assert.Equal(-45f, saved.RotationEulerDegrees[0], 1);
        Assert.Equal(10f, saved.RotationEulerDegrees[1], 1);

        // 度数を優先させるため、古いクォータニオン指定は消える。
        Assert.Null(saved.RotationQuaternion);

        // 触れていない項目は元のまま。幅は SettingsField.OverlayWidth を指定しない限り変わらない。
        Assert.Equal(0.5f, saved.BackgroundOpacity);
        Assert.Equal(60f, saved.IdFontPixels);
        Assert.Equal(0.14f, saved.OverlayWidthMeters);
        Assert.Contains("// 説明", File.ReadAllText(path));
    }

    /// <summary>複数の項目を指定すれば、どれも書き戻す（位置と幅）。</summary>
    [Fact]
    public void 位置合わせの保存は幅も書き戻す()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        File.WriteAllText(path, """
            {
              "backgroundOpacity": 0.5,
              "overlayWidthMeters": 0.14
            }
            """);

        var settings = AppSettings.Load(path, new CollectingDiagnostics());
        settings.SetPlacement(new Vector3(0.1f, 0.2f, 0.3f), Rotations.FromEulerDegrees(-45f, 0f, 0f));
        settings.OverlayWidthMeters = 0.2f;

        settings.SaveFields(path, SettingsField.Placement | SettingsField.OverlayWidth);

        var saved = AppSettings.Load(path, new CollectingDiagnostics());

        Assert.Equal(0.2f, saved.OverlayWidthMeters);
        Assert.Equal([0.1f, 0.2f, 0.3f], saved.TranslationMeters);
        Assert.Equal(0.5f, saved.BackgroundOpacity);
    }

    /// <summary>設定ファイルがまだない環境では、全項目を書き出して作る。</summary>
    [Fact]
    public void 設定ファイルがなければ保存で作られる()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        var settings = new AppSettings();
        settings.SetPlacement(new Vector3(0.1f, 0.2f, 0.3f), Rotations.FromEulerDegrees(-45f, 0f, 0f));

        settings.SaveFields(path, SettingsField.Placement);

        var saved = AppSettings.Load(path, new CollectingDiagnostics());

        Assert.Equal([0.1f, 0.2f, 0.3f], saved.TranslationMeters);
        Assert.Equal(0.9f, saved.BackgroundOpacity);
    }

    /// <summary>
    /// 利用者が書き損じて JSON として読めない設定ファイルは、黙って上書きしない。
    /// 別の名前へ移して残し、いまの設定で作り直す（手で書いた値やコメントを失わせない）。
    /// </summary>
    [Fact]
    public void 読めない設定ファイルは移して残してから作り直す()
    {
        using var tempDirectory = new TempDirectory("broken");
        var directory = tempDirectory.Path;
        var path = Path.Combine(directory, "settings.json");
        const string broken = """
            {
              // 手で書いた設定（閉じ括弧を書き忘れた）
              "overlayWidthMeters": 0.2,
            """;

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, broken);

        var log = new CollectingDiagnostics();
        var settings = AppSettings.Load(path, log);
        Assert.Contains(log.Messages, m => m.StartsWith("ERROR"));

        settings.OverlayWidthMeters = 0.16f;
        settings.SaveFields(path, SettingsField.OverlayWidth, log);

        // 元の中身は別の名前で残る。
        var backup = Assert.Single(Directory.GetFiles(directory, "settings.json.broken-*"));
        Assert.Equal(broken, File.ReadAllText(backup));
        Assert.Contains(log.Messages, m => m.StartsWith("WARN") && m.Contains(Path.GetFileName(backup)));

        // 設定ファイルは読める形で作り直される。
        var reloaded = new CollectingDiagnostics();
        Assert.Equal(0.16f, AppSettings.Load(path, reloaded).OverlayWidthMeters);
        Assert.DoesNotContain(reloaded.Messages, m => m.StartsWith("ERROR") || m.StartsWith("WARN"));
    }

    /// <summary>
    /// 2026-09-30に外した項目（textureSubmit・panelUpdateHz・flipTextureVertically・premultipliedAlpha）が
    /// 残っている古い settings.json も、警告なしでそのまま読める（知らない項目は読み飛ばす）。
    /// </summary>
    [Fact]
    public void 外した項目が残っている設定ファイルも警告なしで読める()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;
        File.WriteAllText(path, """
            {
              "textureSubmit": "sharedHandle",
              "panelUpdateHz": 30,
              "flipTextureVertically": false,
              "premultipliedAlpha": false,
              "backgroundOpacity": 0.8
            }
            """);

        var log = new CollectingDiagnostics();
        var settings = AppSettings.Load(path, log);

        Assert.DoesNotContain(log.Messages, m => m.StartsWith("WARN") || m.StartsWith("ERROR"));
        Assert.Equal(0.8f, settings.BackgroundOpacity);
    }
}
