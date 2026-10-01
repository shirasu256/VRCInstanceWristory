using System.Numerics;
using Valve.VR;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// OpenVRの3×4行列（行優先、列0～2が基底軸、列3が並進）と System.Numerics の相互変換。
/// OpenVRの姿勢は -Z が前方。
/// </summary>
public static class Math3d
{
    public static Vector3 Translation(in HmdMatrix34_t m) => new(m.m3, m.m7, m.m11);

    public static Vector3 Right(in HmdMatrix34_t m) => new(m.m0, m.m4, m.m8);

    public static Vector3 Up(in HmdMatrix34_t m) => new(m.m1, m.m5, m.m9);

    /// <summary>行列のZ軸（後方）。前方は -Z。</summary>
    public static Vector3 Back(in HmdMatrix34_t m) => new(m.m2, m.m6, m.m10);

    public static Vector3 Forward(in HmdMatrix34_t m) => -Back(m);

    public static HmdMatrix34_t FromAxes(Vector3 x, Vector3 y, Vector3 z, Vector3 translation) => new()
    {
        m0 = x.X, m1 = y.X, m2 = z.X, m3 = translation.X,
        m4 = x.Y, m5 = y.Y, m6 = z.Y, m7 = translation.Y,
        m8 = x.Z, m9 = y.Z, m10 = z.Z, m11 = translation.Z,
    };

    /// <summary>回転してから並進する（T * R）。</summary>
    public static HmdMatrix34_t FromTransform(Vector3 translation, Quaternion rotation)
    {
        var q = Quaternion.Normalize(rotation);
        return FromAxes(
            Vector3.Transform(Vector3.UnitX, q),
            Vector3.Transform(Vector3.UnitY, q),
            Vector3.Transform(Vector3.UnitZ, q),
            translation);
    }

    /// <summary>a の座標系で表された b を、a の親座標系へ移す（a * b）。</summary>
    public static HmdMatrix34_t Multiply(in HmdMatrix34_t a, in HmdMatrix34_t b)
    {
        var ax = Right(a);
        var ay = Up(a);
        var az = Back(a);
        var at = Translation(a);

        Vector3 Rotate(Vector3 v) => ax * v.X + ay * v.Y + az * v.Z;

        return FromAxes(
            Rotate(Right(b)),
            Rotate(Up(b)),
            Rotate(Back(b)),
            at + Rotate(Translation(b)));
    }

    /// <summary>剛体変換の逆（回転は転置、並進は符号反転して回す）。</summary>
    public static HmdMatrix34_t Invert(in HmdMatrix34_t m)
    {
        var x = Right(m);
        var y = Up(m);
        var z = Back(m);
        var t = Translation(m);

        // 転置した基底（= 逆回転）
        var ix = new Vector3(x.X, y.X, z.X);
        var iy = new Vector3(x.Y, y.Y, z.Y);
        var iz = new Vector3(x.Z, y.Z, z.Z);

        var it = -new Vector3(Vector3.Dot(x, t), Vector3.Dot(y, t), Vector3.Dot(z, t));
        return FromAxes(ix, iy, iz, it);
    }

    /// <summary>
    /// 世界座標の点を、剛体変換 <paramref name="m"/> の座標系へ移す（m の逆変換を点に適用する）。
    /// レイとパネルの交点から「パネルのどこを指しているか」を求めるのに使う。
    /// </summary>
    public static Vector3 InverseTransformPoint(in HmdMatrix34_t m, Vector3 point)
    {
        var d = point - Translation(m);
        return new Vector3(Vector3.Dot(d, Right(m)), Vector3.Dot(d, Up(m)), Vector3.Dot(d, Back(m)));
    }

    /// <summary>回転部分をクォータニオンとして取り出す。</summary>
    public static Quaternion ToQuaternion(in HmdMatrix34_t m)
    {
        var x = Right(m);
        var y = Up(m);
        var z = Back(m);

        // System.Numerics は行ベクトル規約なので、各基底の像を行に入れる。
        var matrix = new Matrix4x4(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            0f, 0f, 0f, 1f);

        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
    }

    public static HmdVector3_t ToHmd(Vector3 v) => new() { v0 = v.X, v1 = v.Y, v2 = v.Z };

    public static Vector3 FromHmd(in HmdVector3_t v) => new(v.v0, v.v1, v.v2);

    /// <summary>与えた法線方向を向き、指定の上方向にできるだけ沿う姿勢を作る。</summary>
    public static HmdMatrix34_t LookAlong(Vector3 position, Vector3 normal, Vector3 preferredUp)
    {
        var z = Normalize(normal, Vector3.UnitZ);
        var up = preferredUp - z * Vector3.Dot(preferredUp, z);

        if (up.LengthSquared() < 1e-8f)
            up = Math.Abs(z.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;

        var y = Normalize(up, Vector3.UnitY);
        var x = Vector3.Normalize(Vector3.Cross(y, z));
        return FromAxes(x, y, z, position);
    }

    public static Vector3 Normalize(Vector3 v, Vector3 fallback)
    {
        var length = v.Length();
        return length < 1e-6f ? fallback : v / length;
    }
}
