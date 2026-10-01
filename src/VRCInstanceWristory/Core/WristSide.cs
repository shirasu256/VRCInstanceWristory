using System.Numerics;

namespace VRCInstanceWristory.Core;

/// <summary>
/// パネルを付ける手首（設定 <c>wristSide</c>・2026-09-26のユーザー指定→実装メモ5.49）。
/// 操作する手（レイ・スティック・グリップ・トリガー）は、付けた手首と反対の手になる。
/// </summary>
public enum WristSide
{
    /// <summary>左手首に付け、右手で操作する（既定・仕様Q01）。</summary>
    Left,

    /// <summary>右手首に付け、左手で操作する。</summary>
    Right,
}

public static class WristSides
{
    /// <summary>設定ファイルに書く名前。</summary>
    public static string SettingName(WristSide side) => side == WristSide.Right ? "right" : "left";

    /// <summary>設定ファイルの名前を読む。大文字小文字は問わない。知らない名前は null。</summary>
    public static WristSide? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "left" => WristSide.Left,
        "right" => WristSide.Right,
        _ => null,
    };

    /// <summary>画面に出す名前。</summary>
    public static string DisplayName(WristSide side) => side == WristSide.Right ? "右手首" : "左手首";

    /// <summary>操作する手（パネルを付けた手首の反対）。</summary>
    public static WristSide Opposite(WristSide side) => side == WristSide.Right ? WristSide.Left : WristSide.Right;

    /// <summary>
    /// 左手首での置き方を、右手首で同じ見え方になる置き方へ直す（左右の鏡映し）。
    ///
    /// 位置はコントローラーの左右（X）だけを反転する。姿勢は「左右を反転した回転」M·R·M（M = X の反転）で、
    /// これは回転のまま（鏡像にはならない）なので、パネルの文字は裏返らない。
    /// クォータニオンでは (x, y, z, w) → (x, −y, −z, w)、度数では pitch はそのまま・yaw と roll の符号が反転する。
    /// 左右の手のコントローラーは鏡映しの形なので、左で合わせた置き方がそのまま右の出発点になる。
    /// </summary>
    public static Vector3 Mirror(Vector3 translation) => new(-translation.X, translation.Y, translation.Z);

    public static Quaternion Mirror(Quaternion rotation)
        => Quaternion.Normalize(new Quaternion(rotation.X, -rotation.Y, -rotation.Z, rotation.W));
}
