using System.Numerics;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>無効な値を既定値へ戻す（仕様10節）。VR内にはエラーを出さず、項目名をPC側へ知らせる。</summary>
public sealed partial class AppSettings
{
    /// <summary>無効な項目を既定値へ戻し、項目名をPC側へ知らせる。</summary>
    public void Validate(IDiagnostics log)
    {
        var defaults = new AppSettings();

        if (TranslationMeters is not { Length: 3 } || TranslationMeters.Any(v => !float.IsFinite(v)))
        {
            log.Warn("設定 translationMeters が無効です（3要素の有限値が必要）。既定値を使います。");
            TranslationMeters = defaults.TranslationMeters;
        }

        if (RotationEulerDegrees is not { Length: 3 }
            || RotationEulerDegrees.Any(v => !float.IsFinite(v) || Math.Abs(v) > 360f))
        {
            log.Warn("設定 rotationEulerDegrees が無効です（-360〜360 の3要素 [pitch, yaw, roll] が必要）。既定値を使います。");
            RotationEulerDegrees = defaults.RotationEulerDegrees;
        }

        if (RotationQuaternion is not null
            && (RotationQuaternion is not { Length: 4 }
                || RotationQuaternion.Any(v => !float.IsFinite(v))
                || Math.Abs(new Quaternion(RotationQuaternion[0], RotationQuaternion[1], RotationQuaternion[2], RotationQuaternion[3]).Length() - 1f) > 0.01f))
        {
            log.Warn("設定 rotationQuaternion が無効です（正規化された4要素）。rotationEulerDegrees を使います。");
            RotationQuaternion = null;
        }

        OverlayWidthMeters = Require(OverlayWidthMeters, v => float.IsFinite(v) && v > 0f, defaults.OverlayWidthMeters, nameof(OverlayWidthMeters), log);
        // 下限は50%（2026-09-29のユーザー指定→実装メモ5.89。それまでは15%）。
        BackgroundOpacity = Require(BackgroundOpacity, v => v is >= 0.5f and <= 1f, defaults.BackgroundOpacity, nameof(BackgroundOpacity), log);
        IdFontPixels = Require(IdFontPixels, v => v is > 4f and <= 200f, defaults.IdFontPixels, nameof(IdFontPixels), log);
        AuxFontPixels = Require(AuxFontPixels, v => v is > 4f and <= 200f, defaults.AuxFontPixels, nameof(AuxFontPixels), log);
        ScrollDeadzone = Require(ScrollDeadzone, v => v is >= 0f and < 1f, defaults.ScrollDeadzone, nameof(ScrollDeadzone), log);
        ScrollRowsPerSecond = Require(ScrollRowsPerSecond, v => v is > 0f and <= 60f, defaults.ScrollRowsPerSecond, nameof(ScrollRowsPerSecond), log);
        CursorSizeMeters = Require(CursorSizeMeters, v => v is > 0f and <= 0.5f, defaults.CursorSizeMeters, nameof(CursorSizeMeters), log);
        ViewAngleLimitDegrees = Require(ViewAngleLimitDegrees, v => v is >= 5f and <= 90f, defaults.ViewAngleLimitDegrees, nameof(ViewAngleLimitDegrees), log);
        ViewAngleFadeSeconds = Require(ViewAngleFadeSeconds, v => v is >= 0f and <= 5f, defaults.ViewAngleFadeSeconds, nameof(ViewAngleFadeSeconds), log);

        // 予告のアイコン（→実装メモ5.89）。選べる値だけを受け付ける。
        ResetWarningScale = Require(ResetWarningScale, v => ResetWarningOptions.Scales.Any(s => MathF.Abs(s - v) < 0.001f), defaults.ResetWarningScale, nameof(ResetWarningScale), log);
        ResetWarningOpacity = Require(ResetWarningOpacity, v => v is >= 0.5f and <= 1f, defaults.ResetWarningOpacity, nameof(ResetWarningOpacity), log);
        ResetWarningMicOffsetXCm = Require(ResetWarningMicOffsetXCm, v => float.IsFinite(v) && MathF.Abs(v) <= ResetWarningOptions.MicOffsetLimitCm, 0f, nameof(ResetWarningMicOffsetXCm), log);
        ResetWarningMicOffsetYCm = Require(ResetWarningMicOffsetYCm, v => float.IsFinite(v) && MathF.Abs(v) <= ResetWarningOptions.MicOffsetLimitCm, 0f, nameof(ResetWarningMicOffsetYCm), log);
        ResetWarningMicOffsetZCm = Require(ResetWarningMicOffsetZCm, v => float.IsFinite(v) && MathF.Abs(v) <= ResetWarningOptions.MicDepthLimitCm, 0f, nameof(ResetWarningMicOffsetZCm), log);

        if (!ResetWarningOptions.BlinkCounts.Contains(ResetWarningBlinkCount))
        {
            log.Warn($"設定 resetWarningBlinkCount が無効です（{string.Join(" / ", ResetWarningOptions.BlinkCounts)}）。既定値 {defaults.ResetWarningBlinkCount} を使います。");
            ResetWarningBlinkCount = defaults.ResetWarningBlinkCount;
        }

        if (!ResetWarningOptions.LeadMinutes.Contains(ResetWarningLeadMinutes))
        {
            log.Warn($"設定 resetWarningLeadMinutes が無効です（{string.Join(" / ", ResetWarningOptions.LeadMinutes)}）。既定値 {defaults.ResetWarningLeadMinutes} を使います。");
            ResetWarningLeadMinutes = defaults.ResetWarningLeadMinutes;
        }

        if (ResetWarningPositions.Parse(ResetWarningPosition) is not { } warningPosition)
        {
            log.Warn($"設定 resetWarningPosition が無効です（{string.Join(" / ", ResetWarningPositions.Order.Select(ResetWarningPositions.SettingName))}）。既定値（bottomLeft）を使います。");
            ResetWarningPosition = defaults.ResetWarningPosition;
        }
        else
        {
            ResetWarningPosition = ResetWarningPositions.SettingName(warningPosition);
        }

        if (RetentionMinutes is < HistoryStore.MinRetentionMinutes or > HistoryStore.MaxRetentionMinutes)
        {
            log.Warn($"設定 retentionMinutes が無効です（{HistoryStore.MinRetentionMinutes}〜{HistoryStore.MaxRetentionMinutes}の分数）。既定値 {defaults.RetentionMinutes} を使います。");
            RetentionMinutes = defaults.RetentionMinutes;
        }

        var unknownTypes = (TargetAccessTypes ?? []).Where(n => Core.Locations.TargetAccessTypes.ParseSettingName(n) is null).ToList();

        if (unknownTypes.Count > 0)
            log.Warn($"設定 targetAccessTypes に知らない種類があります（無視します）: {string.Join(", ", unknownTypes)}");

        if (TargetTypes.Count == 0)
        {
            log.Warn("設定 targetAccessTypes に記録する種類がありません。既定値（Public / GroupPublic / GroupPlus / GroupOnly）を使います。");
            TargetAccessTypes = defaults.TargetAccessTypes;
        }
        else
        {
            // 綴りと並びをそろえておく（ウィンドウから書き戻したときと同じ形）。
            SetTargetTypes(TargetTypes);
        }

        if (WristSides.Parse(WristSide) is not { } side)
        {
            log.Warn("設定 wristSide が無効です（left か right）。既定値（left）を使います。");
            WristSide = defaults.WristSide;
        }
        else
        {
            WristSide = WristSides.SettingName(side);
        }

        if (RightTranslationMeters is not null
            && (RightTranslationMeters is not { Length: 3 } || RightTranslationMeters.Any(v => !float.IsFinite(v))))
        {
            log.Warn("設定 rightTranslationMeters が無効です（3要素の有限値）。左手首の配置を左右反転して使います。");
            RightTranslationMeters = null;
        }

        if (RightRotationEulerDegrees is not null
            && (RightRotationEulerDegrees is not { Length: 3 } || RightRotationEulerDegrees.Any(v => !float.IsFinite(v) || Math.Abs(v) > 360f)))
        {
            log.Warn("設定 rightRotationEulerDegrees が無効です（-360〜360 の3要素）。左手首の配置を左右反転して使います。");
            RightRotationEulerDegrees = null;
        }

        if (ReturnActions.Parse(ReturnAction) is not { } action)
        {
            log.Warn("設定 returnAction が無効です（browser か vrchat）。既定値（browser）を使います。");
            ReturnAction = defaults.ReturnAction;
        }
        else
        {
            ReturnAction = ReturnActions.SettingName(action);
        }

        // photos（Windows のフォト）は 2026-09-27 になくした（→実装メモ5.58）。前の版が書いた値なので、警告せずに既定へ直す。
        if (string.Equals(PhotoViewer?.Trim(), "photos", StringComparison.OrdinalIgnoreCase))
            PhotoViewer = defaults.PhotoViewer;

        if (PhotoViewers.Parse(PhotoViewer) is not { } viewer)
        {
            log.Warn("設定 photoViewer が無効です（default / custom）。既定値（default）を使います。");
            PhotoViewer = defaults.PhotoViewer;
        }
        else
        {
            PhotoViewer = PhotoViewers.SettingName(viewer);
        }

        if (string.IsNullOrWhiteSpace(PhotoViewerPath))
            PhotoViewerPath = null;

        if (Viewer == PhotoViewerKind.Custom && PhotoViewerPath is null)
        {
            log.Warn("設定 photoViewer が custom ですが、photoViewerPath がありません。既定のアプリで開きます。");
            PhotoViewer = defaults.PhotoViewer;
        }

        GroupNames = ValidGroupNames(GroupNames, log);
    }

    /// <summary>グループ名に使える長さ。3段目の幅に収まる程度の自分用の呼び名を想定する（→実装メモ5.48）。</summary>
    public const int MaxGroupNameLength = 40;

    /// <summary>
    /// グループ名の表を確かめる。Group ID として読めない鍵・空の名前・長すぎる名前・制御文字を含む名前は、知らせて捨てる。
    /// 名前の前後の空白は落とす。
    /// </summary>
    private static Dictionary<string, string> ValidGroupNames(Dictionary<string, string>? names, IDiagnostics log)
    {
        var valid = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (groupId, name) in names ?? [])
        {
            var trimmed = NormalizeGroupName(name);

            if (!LocationParser.IsValidGroupId(groupId ?? string.Empty) || trimmed is null)
            {
                log.Warn($"設定 groupNames の「{groupId}」は使えません（grp_ で始まる Group ID と、{MaxGroupNameLength}文字までの名前）。無視します。");
                continue;
            }

            valid[groupId!] = trimmed;
        }

        return valid;
    }

    /// <summary>付けようとしている名前を整える。使えない名前なら null（空・長すぎる・制御文字を含む）。</summary>
    public static string? NormalizeGroupName(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxGroupNameLength || trimmed.Any(char.IsControl))
            return null;

        return trimmed;
    }

    private static float Require(float value, Func<float, bool> valid, float fallback, string name, IDiagnostics log)
    {
        if (valid(value))
            return value;

        log.Warn($"設定 {char.ToLowerInvariant(name[0]) + name[1..]} が無効です。既定値 {fallback} を使います。");
        return fallback;
    }
}
