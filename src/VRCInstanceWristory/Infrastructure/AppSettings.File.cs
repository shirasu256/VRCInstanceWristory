using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// 設定ファイルの読み書き。書き戻すときは変えた項目（<see cref="SettingsField"/>）だけを置き換え、
/// 利用者が書いたほかの値・説明用のキー・並び順はそのまま残す。
/// </summary>
public sealed partial class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 書き戻す対象ごとの、設定ファイルの項目（プロパティ名）。
    /// <see cref="SettingsField.LaunchWithSteamVr"/> / <see cref="SettingsField.LaunchAtLogon"/> は設定ファイルには書かない
    /// （正本はSteamVRとWindowsの登録）ので、ここにはない。
    /// </summary>
    private static readonly (SettingsField Field, string[] Properties)[] FieldProperties =
    [
        // 度数で書き戻すので、優先されてしまう古いクォータニオン指定も書く（SetPlacement が消してある）。
        (SettingsField.Placement, [nameof(TranslationMeters), nameof(RotationEulerDegrees), nameof(RotationQuaternion)]),
        (SettingsField.OverlayWidth, [nameof(OverlayWidthMeters)]),
        (SettingsField.RetentionMinutes, [nameof(RetentionMinutes)]),
        (SettingsField.TargetAccessTypes, [nameof(TargetAccessTypes)]),
        (SettingsField.BackgroundOpacity, [nameof(BackgroundOpacity)]),
        (SettingsField.ViewAngle, [nameof(ViewAngleLimitDegrees), nameof(ViewAngleFadeSeconds)]),
        (SettingsField.ScrollSpeed, [nameof(ScrollRowsPerSecond)]),
        (SettingsField.DesktopWindow, [nameof(DesktopWindowTopMost)]),

        // 書いていない（null）なら左の配置を左右反転して使う、という意味なので、null もそのまま書く。
        (SettingsField.RightPlacement, [nameof(RightTranslationMeters), nameof(RightRotationEulerDegrees)]),
        (SettingsField.WristSide, [nameof(WristSide)]),
        (SettingsField.ReturnAction, [nameof(ReturnAction)]),
        (SettingsField.PhotoViewer, [nameof(PhotoViewer), nameof(PhotoViewerPath)]),
        (SettingsField.GroupDisplay, [nameof(ShowGroupIdWithName)]),
        (SettingsField.LoadingScreen, [nameof(ShowPanelDuringLoading)]),
        (SettingsField.VrOverlay, [nameof(VrOverlayEnabled)]),
        (SettingsField.AutoReset, [nameof(AutoResetEnabled)]),
        (SettingsField.Vibration, [nameof(ControllerVibrationEnabled)]),
        (SettingsField.PanelGrab, [nameof(PanelGrabEnabled)]),
        (SettingsField.ExternalReset, [nameof(ExternalResetEnabled)]),
        (SettingsField.TriggerMenu, [nameof(TriggerMenuEnabled)]),
        (SettingsField.ResetWarning,
        [
            nameof(ResetWarningEnabled),
            nameof(ResetWarningPosition),
            nameof(ResetWarningScale),
            nameof(ResetWarningOpacity),
            nameof(ResetWarningBlinkCount),
            nameof(ResetWarningLeadMinutes),
            nameof(ResetWarningMicOffsetXCm),
            nameof(ResetWarningMicOffsetYCm),
            nameof(ResetWarningMicOffsetZCm),
            nameof(ResetWarningReshowAfterAfk),
        ]),
        (SettingsField.AfkPause, [nameof(PauseCountdownWhileAfk)]),
        (SettingsField.TargetPause, [nameof(StopCountdownInTarget)]),
        (SettingsField.AfkDetection, [nameof(AfkDetectionEnabled)]),
        (SettingsField.Welcome, [nameof(WelcomeCompleted)]),
        (SettingsField.GroupNames, [nameof(GroupNames)]),
    ];

    /// <summary>
    /// 設定ファイルを読み、無効な値を既定値へ戻す（<see cref="Validate"/> はここで済ませる）。
    /// ないとき・読めないときは既定値で動く。
    /// </summary>
    public static AppSettings Load(string path, IDiagnostics log)
    {
        if (!File.Exists(path))
        {
            log.Info($"設定ファイルがないので既定値で動作します: {path}");
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            MigrateAfkDetection(settings, json);
            settings.Validate(log);
            return settings;
        }
        catch (Exception ex)
        {
            log.Error($"設定ファイルを読めません（既定値で動作します）: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <summary>
    /// afkDetectionEnabled を足す前（→実装メモ5.98）の設定ファイルには、この項目がない。そのときは、それまでの決まり
    /// （AFK を使う設定のどれかがオンなら受け口を開く）で決める。すでに AFK を使っていた利用者の動きを変えないため。
    /// </summary>
    private static void MigrateAfkDetection(AppSettings settings, string json)
    {
        try
        {
            if (JsonNode.Parse(json, documentOptions: DocumentOptions) is JsonObject root
                && root.Any(pair => string.Equals(pair.Key, nameof(AfkDetectionEnabled), StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
        }
        catch (JsonException)
        {
            return;
        }

        settings.AfkDetectionEnabled = settings.PauseCountdownWhileAfk || (settings.ResetWarningEnabled && settings.ResetWarningReshowAfterAfk);
    }

    /// <summary>全項目を書き出す。設定ファイルがまだないときの作成にだけ使う。</summary>
    public void Save(string path)
    {
        // 書いている途中で電源が落ちても、途中で切れた設定ファイルを残さない（→実装メモ5.59）。
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    /// <summary>
    /// 指定した項目だけを設定ファイルへ書き戻す。触れなかった項目は、利用者が書いた値も
    /// 説明用のキーもそのまま残る。
    /// 以前は掴んで置き直すたびに全項目を書き出していたため、一度でも保存すると全設定が
    /// その時点の値で固定され、あとからコード側の既定値を変えても反映されなくなっていた
    /// （2026-09-19に背景の不透明度で顕在化）。
    ///
    /// 設定ファイルがまだなければ全項目で作る。あるのに JSON のオブジェクトとして読めない（利用者が書き損じた）ときは、
    /// 黙って上書きせず、<c>settings.json.broken-日時</c> へ移して残してから全項目で作り直す。
    /// 書けなければ例外を投げる。
    /// </summary>
    public void SaveFields(string path, SettingsField fields, IDiagnostics? log = null)
    {
        if (fields == SettingsField.None)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var root = ReadObject(path);

        if (root is null)
        {
            if (File.Exists(path))
                MoveAsideBroken(path, log);

            Save(path);
            return;
        }

        // いまの値を全項目ぶん JSON にしてから、指定した項目だけを写す。
        var values = JsonSerializer.SerializeToNode(this, Options)!.AsObject();

        // グループ名は Group ID の順に並べて書く（付けた順にしない）。
        values[JsonNamingPolicy.CamelCase.ConvertName(nameof(GroupNames))] = SortedGroupNames();

        foreach (var (field, properties) in FieldProperties)
        {
            if (!fields.HasFlag(field))
                continue;

            foreach (var property in properties)
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(property);
                SetValue(root, name, values[name]?.DeepClone());
            }
        }

        AtomicFile.WriteAllText(path, root.ToJsonString(Options));
    }

    /// <summary>
    /// <see cref="SaveFields"/> を試す。書けなければ「<paramref name="failure"/>: 理由」を PC 側へ出し、false と理由を返す。
    /// </summary>
    public bool TrySaveFields(string path, SettingsField fields, IDiagnostics log, string failure, out string? error)
    {
        try
        {
            SaveFields(path, fields, log);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"{failure}: {ex.Message}");
            error = ex.Message;
            return false;
        }
    }

    private JsonObject SortedGroupNames()
    {
        var names = new JsonObject();

        foreach (var (groupId, name) in GroupNames.OrderBy(p => p.Key, StringComparer.Ordinal))
            names[groupId] = JsonValue.Create(name);

        return names;
    }

    /// <summary>設定ファイルを JSON のオブジェクトとして読む。ないとき・読めないときは null。</summary>
    private static JsonObject? ReadObject(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: DocumentOptions) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 読めない設定ファイルを、上書きする前に <c>…broken-yyyyMMdd-HHmmss</c> へ移して残す。
    /// 移せなければ例外を投げる（上書きせずに保存を失敗させる）。
    /// </summary>
    private static void MoveAsideBroken(string path, IDiagnostics? log)
    {
        var backup = $"{path}.broken-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        File.Move(path, backup);
        log?.Warn($"設定ファイルを JSON として読めないので、上書きせずに {backup} へ移して、いまの設定で作り直しました。");
    }

    /// <summary>元のファイルにある綴りと並び順を保ったまま値を置き換える。</summary>
    private static void SetValue(JsonObject root, string camelCase, JsonNode? value)
    {
        var existing = root.FirstOrDefault(pair => string.Equals(pair.Key, camelCase, StringComparison.OrdinalIgnoreCase)).Key;

        root[existing ?? camelCase] = value;
    }
}
