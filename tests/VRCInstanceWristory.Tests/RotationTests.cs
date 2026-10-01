using System.Numerics;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 位置合わせで使う姿勢の変換。度数とクォータニオンを往復しても値が変わらないことを確かめる。
/// （--calibrate で掴んだ結果を度数で保存し、次回それを読み直すため。）
/// </summary>
public class RotationTests
{
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(-70f, 0f, 0f)]
    [InlineData(-62.5f, 8f, -3f)]
    [InlineData(30f, -120f, 45f)]
    [InlineData(-15f, 179f, -179f)]
    public void 度数とクォータニオンを往復できる(float pitch, float yaw, float roll)
    {
        var q = Rotations.FromEulerDegrees(pitch, yaw, roll);
        var (p2, y2, r2) = Rotations.ToEulerDegrees(q);

        Assert.Equal(pitch, Rotations.Normalize(p2), 2);
        Assert.Equal(yaw, Rotations.Normalize(y2), 2);
        Assert.Equal(roll, Rotations.Normalize(r2), 2);
    }

    [Fact]
    public void 既定の角度はパネルを甲側へ向ける()
    {
        // pitch -70度: パネルの法線（+Z）が上向き寄りになり、上端は指先側（-Z）を向く。
        var q = Rotations.FromEulerDegrees(-70f, 0f, 0f);

        var normal = Vector3.Transform(Vector3.UnitZ, q);
        var up = Vector3.Transform(Vector3.UnitY, q);

        Assert.True(normal.Y > 0.9f, $"法線が上を向いていない: {normal}");
        Assert.True(up.Z < -0.3f, $"上端が指先側を向いていない: {up}");
    }

    [Fact]
    public void 行列とクォータニオンの往復でも姿勢が保たれる()
    {
        var rotation = Rotations.FromEulerDegrees(-40f, 25f, 10f);
        var translation = new Vector3(0.01f, 0.02f, 0.09f);

        var matrix = Math3d.FromTransform(translation, rotation);

        Assert.Equal(translation.X, Math3d.Translation(matrix).X, 5);
        Assert.Equal(translation.Z, Math3d.Translation(matrix).Z, 5);

        var back = Math3d.ToQuaternion(matrix);
        var (p, y, r) = Rotations.ToEulerDegrees(back);

        Assert.Equal(-40f, p, 2);
        Assert.Equal(25f, y, 2);
        Assert.Equal(10f, r, 2);
    }

    [Fact]
    public void 逆変換を掛けると元に戻る()
    {
        var a = Math3d.FromTransform(new Vector3(0.3f, -0.2f, 1.1f), Rotations.FromEulerDegrees(12f, -34f, 56f));
        var identity = Math3d.Multiply(Math3d.Invert(a), a);

        Assert.Equal(0f, Math3d.Translation(identity).X, 4);
        Assert.Equal(0f, Math3d.Translation(identity).Y, 4);
        Assert.Equal(0f, Math3d.Translation(identity).Z, 4);

        Assert.Equal(1f, Math3d.Right(identity).X, 4);
        Assert.Equal(1f, Math3d.Up(identity).Y, 4);
        Assert.Equal(1f, Math3d.Back(identity).Z, 4);
    }

    [Fact]
    public void 掴んで動かす計算は右手の動きぶんだけパネルを動かす()
    {
        // 左手・右手・パネルの初期配置。
        var leftPose = Math3d.FromTransform(new Vector3(0f, 1f, 0f), Quaternion.Identity);
        var rightPose = Math3d.FromTransform(new Vector3(0.3f, 1f, 0f), Quaternion.Identity);
        var relative = Math3d.FromTransform(new Vector3(0f, 0.02f, 0.09f), Rotations.FromEulerDegrees(-70f, 0f, 0f));

        // 掴んだ瞬間の、右手から見たパネルの位置関係。
        var panelWorld = Math3d.Multiply(leftPose, relative);
        var grabOffset = Math3d.Multiply(Math3d.Invert(rightPose), panelWorld);

        // 右手を5cm上へ動かす。
        var movedRight = Math3d.FromTransform(new Vector3(0.3f, 1.05f, 0f), Quaternion.Identity);
        var movedPanel = Math3d.Multiply(movedRight, grabOffset);
        var newRelative = Math3d.Multiply(Math3d.Invert(leftPose), movedPanel);

        var before = Math3d.Translation(relative);
        var after = Math3d.Translation(newRelative);

        Assert.Equal(before.X, after.X, 4);
        Assert.Equal(before.Y + 0.05f, after.Y, 4);
        Assert.Equal(before.Z, after.Z, 4);

        // 回転は変わらない。
        var (p, y, r) = Rotations.ToEulerDegrees(Math3d.ToQuaternion(newRelative));
        Assert.Equal(-70f, p, 2);
        Assert.Equal(0f, y, 2);
        Assert.Equal(0f, r, 2);
    }
}
