using System.Diagnostics;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// URL・写真・フォルダーを Windows に開いてもらう。
///
/// どれも主ループの外の STA のスレッドで行い、呼び出し元には待たせない。
/// ブラウザや写真を開くアプリの起動は時間がかかることがあり、主ループ（VRの描画）を止めないため。
/// ShellExecute は COM を STA で初期化したスレッドから呼ぶことが求められている。
/// 開けなかったときは理由を PC 側へ出すだけで、呼び出し元へは返さない。
/// </summary>
public static class ShellLauncher
{
    /// <summary>URL を既定のアプリ（ブラウザ・<c>vrchat://</c> なら VRChat）で開く。</summary>
    /// <param name="failure">開けなかったときに出す文の頭（「ブラウザで開けません」など）。</param>
    public static void OpenUrl(string url, string failure, IDiagnostics log)
        => RunOnShellThread(() =>
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                log.Error($"{failure}: {ex.Message}");
            }
        });

    /// <summary>
    /// 写真を開く（→実装メモ5.55）。選べるのは、その種類のファイルの既定のアプリと、利用者が選んだ実行ファイルの2つ。
    /// Windows のフォトを名指しで開く選択肢は 2026-09-27 のユーザー指定でなくした（→実装メモ5.58）。
    /// </summary>
    public static void OpenPhoto(string photoPath, PhotoViewerKind kind, string? customPath, IDiagnostics log)
        => RunOnShellThread(() => OpenPhotoNow(photoPath, kind, customPath, log));

    /// <summary>写真のフォルダーを、その写真を選んだ状態でエクスプローラーで開く（→実装メモ5.47）。写真がなければフォルダーだけを開く。</summary>
    public static void ShowInFolder(string path, IDiagnostics log)
        => RunOnShellThread(() =>
        {
            try
            {
                if (File.Exists(path))
                {
                    using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
                    return;
                }

                if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder))
                {
                    using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
                    return;
                }

                log.Warn($"写真が見つかりません（移動・削除された可能性があります）: {path}");
            }
            catch (Exception ex)
            {
                log.Error($"写真のフォルダーを開けません: {ex.Message}");
            }
        });

    private static void OpenPhotoNow(string photoPath, PhotoViewerKind kind, string? customPath, IDiagnostics log)
    {
        if (!File.Exists(photoPath))
        {
            log.Warn($"写真が見つかりません（移動・削除された可能性があります）: {photoPath}");
            return;
        }

        try
        {
            if (kind == PhotoViewerKind.Custom && customPath is not null && File.Exists(customPath))
            {
                var start = new ProcessStartInfo(customPath) { UseShellExecute = false };
                start.ArgumentList.Add(photoPath);
                using var custom = Process.Start(start);
                return;
            }

            if (kind == PhotoViewerKind.Custom)
                log.Warn($"写真を開くアプリが見つかりません（{customPath ?? "未設定"}）。既定のアプリで開きます。");

            using var process = Process.Start(new ProcessStartInfo(photoPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            log.Error($"写真を開けません: {ex.Message}");
        }
    }

    private static void RunOnShellThread(Action action)
    {
        var thread = new Thread(() => action())
        {
            Name = "ShellOpen",
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
