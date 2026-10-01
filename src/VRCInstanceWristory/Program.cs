using System.Text;
using VRCInstanceWristory.Cli;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;

namespace VRCInstanceWristory;

public static class Program
{
    public static int Main(string[] args)
    {
        // Windowsのアプリなのでコンソールは持たない。出力をどこへ向けるかを、Console へ触る前に決める（→実装メモ5.63）。
        ConsoleHost.Attach();

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // リダイレクト先によっては設定できない。出力自体は続行する。
        }

        var options = CommandLine.Parse(args);

        if (options.ShowHelp)
        {
            CommandLine.PrintUsage();
            return ExitCode.Success;
        }

        try
        {
            return options.Mode switch
            {
                AppMode.RenderSample => RenderSampleMode.Run(options.Sample, options.OutputFile!),
                AppMode.ExportIcon => ExportIcon(options.OutputFile!),
                _ => LiveMode.Run(options),
            };
        }
        catch (Exception ex)
        {
            // 原因はPC側だけに出す。VR内へは何も出さない。
            Console.Error.WriteLine($"予期しないエラーで終了しました: {ex.GetType().Name}: {ex.Message}");

            if (options.Verbose)
                Console.Error.WriteLine(ex);

            return ExitCode.Failure;
        }
    }

    /// <summary>実行ファイルに埋め込む app.ico を、ダッシュボードのアイコンと同じ図から作る（→実装メモ5.41）。</summary>
    private static int ExportIcon(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Desktop.AppIcon.WriteIco(full);
        Console.WriteLine($"保存先       : {full}（{string.Join(" / ", Desktop.AppIcon.IcoSizes)}px）");
        return ExitCode.Success;
    }
}
