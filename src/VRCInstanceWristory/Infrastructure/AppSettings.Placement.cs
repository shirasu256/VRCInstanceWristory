using System.Numerics;
using System.Text.Json.Serialization;
using VRCInstanceWristory.Core;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>手首ごとのパネルの配置（→実装メモ5.49・5.76）。</summary>
public sealed partial class AppSettings
{
    [JsonIgnore]
    public Vector3 Translation => new(TranslationMeters[0], TranslationMeters[1], TranslationMeters[2]);

    /// <summary>クォータニオンの直接指定があればそれを、なければ度数から導く。</summary>
    [JsonIgnore]
    public Quaternion Rotation => RotationQuaternion is { Length: 4 } q
        ? Quaternion.Normalize(new Quaternion(q[0], q[1], q[2], q[3]))
        : Rotations.FromEulerDegrees(RotationEulerDegrees);

    /// <summary>
    /// その手首での配置（→実装メモ5.49）。左は今までどおり。右は書いてあればそれを、
    /// 書いていなければ左の配置を左右反転したものを使う。
    /// </summary>
    public (Vector3 Translation, Quaternion Rotation) PlacementFor(Core.WristSide side)
    {
        if (side == Core.WristSide.Left)
            return (Translation, Rotation);

        var translation = RightTranslationMeters is { Length: 3 } t
            ? new Vector3(t[0], t[1], t[2])
            : WristSides.Mirror(Translation);

        var rotation = RightRotationEulerDegrees is { Length: 3 } r
            ? Rotations.FromEulerDegrees(r)
            : WristSides.Mirror(Rotation);

        return (translation, rotation);
    }

    /// <summary>その手首での配置を書き戻す項目。</summary>
    public static SettingsField PlacementField(Core.WristSide side)
        => side == Core.WristSide.Right ? SettingsField.RightPlacement : SettingsField.Placement;

    /// <summary>
    /// その手首での配置を決める。右手首は右の項目へ書き、左手首の配置には触れない。
    /// </summary>
    public void SetPlacement(Core.WristSide side, Vector3 translation, Quaternion rotation)
    {
        if (side == Core.WristSide.Left)
        {
            SetPlacement(translation, rotation);
            return;
        }

        RightTranslationMeters = [translation.X, translation.Y, translation.Z];
        RightRotationEulerDegrees = RoundedEuler(rotation);
    }

    /// <summary>
    /// その手首の配置を既定へ戻す。左は既定の値、右は「左の配置を左右反転したもの」へ戻す（右の項目を消す）。
    /// </summary>
    public void ResetPlacement(Core.WristSide side)
    {
        if (side == Core.WristSide.Right)
        {
            RightTranslationMeters = null;
            RightRotationEulerDegrees = null;
            return;
        }

        var defaults = new AppSettings();
        SetPlacement(defaults.Translation, defaults.Rotation);
    }

    private static float[] RoundedEuler(Quaternion rotation)
    {
        var (pitch, yaw, roll) = Rotations.ToEulerDegrees(rotation);

        return
        [
            MathF.Round(Rotations.Normalize(pitch), 2),
            MathF.Round(Rotations.Normalize(yaw), 2),
            MathF.Round(Rotations.Normalize(roll), 2),
        ];
    }

    /// <summary>左手首の配置を書き戻す。人が読める度数で保存し、クォータニオン指定は消す。</summary>
    public void SetPlacement(Vector3 translation, Quaternion rotation)
    {
        TranslationMeters = [translation.X, translation.Y, translation.Z];
        RotationEulerDegrees = RoundedEuler(rotation);
        RotationQuaternion = null;
    }
}
