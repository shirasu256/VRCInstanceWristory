using System.Drawing;
using System.Drawing.Imaging;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// アプリのアイコン（2026-09-26のユーザー指定→実装メモ5.41）。
///
/// SteamVRのダッシュボードの下のバーに出しているもの（<see cref="SettingsDashboardView.RenderThumbnail"/>）と同じ図を、
/// デスクトップのウィンドウ（タスクバー・Alt+Tab・タスクトレイ）と実行ファイルにも使う。
/// ウィンドウとタスクトレイには、その場の表示倍率に合わせた大きさで描いて渡す。
/// 実行ファイルに埋め込む <c>app.ico</c> は <c>--export-icon</c> でこの図から作ったもので、図を変えたら作り直す。
/// </summary>
public static class AppIcon
{
    /// <summary><c>app.ico</c> に入れる大きさ。Windowsがタスクバー・エクスプローラーで使う大きさをそろえる。</summary>
    public static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    public static Bitmap Render(int size) => SettingsDashboardView.RenderThumbnail(new PanelStyle(), size);

    /// <summary>Win32のアイコン（HICON）を作る。使い終わったら <c>DestroyIcon</c> で離す。</summary>
    public static nint CreateHandle(int size)
    {
        using var bitmap = Render(Math.Max(16, size));
        return bitmap.GetHicon();
    }

    /// <summary>
    /// 複数の大きさを入れた .ico を書く。中身はPNGで持つ（Windows Vista 以降のアイコンが読める形）。
    /// </summary>
    public static void WriteIco(string path)
    {
        var images = IcoSizes.Select(size =>
        {
            using var bitmap = Render(size);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return (Size: size, Data: stream.ToArray());
        }).ToList();

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);

        // ICONDIR
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        // ICONDIRENTRY（256px は 0 と書く決まり）
        var offset = 6 + (16 * images.Count);

        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in images)
            writer.Write(data);
    }
}
