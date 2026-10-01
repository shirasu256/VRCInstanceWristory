using System.Numerics;

namespace VRCInstanceWristory.Core;

/// <summary>
/// 設定で扱う姿勢の表現。人が読み書きするのは度数（pitch / yaw / roll）とし、
/// クォータニオンは内部表現として導出する。
///
/// pitch = X軸まわり（手前・奥へ倒す）、yaw = Y軸まわり（左右へ回す）、roll = Z軸まわり（ひねる）。
/// 適用順は roll → pitch → yaw。
/// </summary>
public static class Rotations
{
    public static Quaternion FromEulerDegrees(float pitch, float yaw, float roll)
        => Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(
            yaw * MathF.PI / 180f,
            pitch * MathF.PI / 180f,
            roll * MathF.PI / 180f));

    public static Quaternion FromEulerDegrees(IReadOnlyList<float> pitchYawRoll)
        => FromEulerDegrees(pitchYawRoll[0], pitchYawRoll[1], pitchYawRoll[2]);

    /// <summary><see cref="FromEulerDegrees(float,float,float)"/> の逆。戻り値は度数。</summary>
    public static (float Pitch, float Yaw, float Roll) ToEulerDegrees(Quaternion rotation)
    {
        var q = Quaternion.Normalize(rotation);
        var x = Vector3.Transform(Vector3.UnitX, q);
        var y = Vector3.Transform(Vector3.UnitY, q);
        var z = Vector3.Transform(Vector3.UnitZ, q);

        var sinPitch = Math.Clamp(-z.Y, -1f, 1f);
        var pitch = MathF.Asin(sinPitch);

        float yaw;
        float roll;

        if (MathF.Abs(sinPitch) > 0.9999f)
        {
            // 真上・真下を向いた特異点。roll を0として yaw に寄せる。
            yaw = MathF.Atan2(-x.Z, x.X);
            roll = 0f;
        }
        else
        {
            yaw = MathF.Atan2(z.X, z.Z);
            roll = MathF.Atan2(x.Y, y.Y);
        }

        const float toDegrees = 180f / MathF.PI;
        return (pitch * toDegrees, yaw * toDegrees, roll * toDegrees);
    }

    /// <summary>-180〜180度へ丸める。表示と保存を安定させる。</summary>
    public static float Normalize(float degrees)
    {
        var value = degrees % 360f;

        if (value > 180f)
            value -= 360f;
        else if (value < -180f)
            value += 360f;

        return value;
    }
}
