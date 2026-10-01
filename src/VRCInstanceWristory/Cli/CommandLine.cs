using System.Globalization;
using VRCInstanceWristory.Desktop;

namespace VRCInstanceWristory.Cli;

/// <summary>
/// コマンドラインの引数を読む。引数の順番によらず同じ結果になる。
/// 知らない引数・値の足りない引数があれば <see cref="AppOptions.ShowHelp"/> を立てて使い方を出させる。
/// </summary>
public static class CommandLine
{
    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        var options = new AppOptions();
        var sample = options.Sample;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "--render-sample":
                    options.Mode = AppMode.RenderSample;
                    options.OutputFile = Value(args, ref i, options);
                    break;

                case "--export-icon":
                    options.Mode = AppMode.ExportIcon;
                    options.OutputFile = Value(args, ref i, options);
                    break;

                case "--window":
                    sample.Target = RenderSampleTarget.Window;
                    break;

                case "--dashboard":
                    sample.Target = RenderSampleTarget.Dashboard;
                    break;

                case "--reset-warning":
                    sample.Target = RenderSampleTarget.ResetWarning;
                    break;

                case "--welcome":
                    sample.Target = RenderSampleTarget.Welcome;
                    break;

                case "--setup":
                    sample.Setup = true;
                    break;

                case "--mark-popup":
                    sample.MarkPopup = true;
                    break;

                case "--reset-confirm":
                    sample.ResetConfirm = true;
                    break;

                case "--vr-error":
                    sample.VrError = true;
                    break;

                case "--tab":
                    sample.Tab = Value(args, ref i, options)?.ToLowerInvariant() switch
                    {
                        "panel" => DesktopTab.Panel,
                        "startup" => DesktopTab.Startup,
                        _ => DesktopTab.Details,
                    };

                    break;

                case "--scroll":
                    sample.ScrollOffset = float.TryParse(Value(args, ref i, options), NumberStyles.Float, CultureInfo.InvariantCulture, out var px) ? px : -1f;
                    break;

                case "--no-window":
                    options.NoWindow = true;
                    break;

                case "--minimized":
                    options.Minimized = true;
                    break;

                case "--from-steamvr":
                    // SteamVR の「起動時に開始するオーバーレイ」から起動された（manifest の arguments）。
                    options.FromSteamVr = true;
                    break;

                case "--log-directory":
                    options.LogDirectory = Value(args, ref i, options);
                    break;

                case "--settings":
                    options.SettingsPath = Value(args, ref i, options);
                    break;

                case "--verbose":
                case "-v":
                    options.Verbose = true;
                    break;

                case "--help":
                case "-h":
                case "-?":
                    options.ShowHelp = true;
                    break;

                default:
                    Console.Error.WriteLine($"不明な引数: {arg}");
                    options.ShowHelp = true;
                    break;
            }
        }

        return options;
    }

    /// <summary>次の引数を値として取る。なければ使い方を出させる。</summary>
    private static string? Value(IReadOnlyList<string> args, ref int i, AppOptions options)
    {
        if (i + 1 < args.Count)
            return args[++i];

        Console.Error.WriteLine($"{args[i]} には値が要ります。");
        options.ShowHelp = true;
        return null;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            VRC Instance Wristory — VRChatの訪問インスタンスIDを手首（既定は左手首）に表示するSteamVRオーバーレイ

            使い方:
              VRCInstanceWristory                       通常動作（ログ監視 + SteamVR表示 + デスクトップのウィンドウ）
              VRCInstanceWristory --render-sample <png> 見た目だけを画像として保存（SteamVRには触れない）
              VRCInstanceWristory --export-icon <ico>   実行ファイルのアイコン（app.ico）を作る（開発用）

            オプション:
              --no-window             通常動作でデスクトップのウィンドウを出さない
              --minimized             ウィンドウをタスクトレイに入れて始める（ログオン時の起動に使う）
              --from-steamvr          SteamVRから起動された（SteamVRの終了と一緒に終わる）
              --log-directory <path>  ログフォルダーの上書き
              --settings <path>       設定ファイルの場所
              --verbose, -v           詳細な診断を表示
              --help, -h              この説明

            --render-sample と併用するもの:
              --window                デスクトップのウィンドウ全体を描く
              --tab <name>            --window の右側のタブ（details / panel / startup）
              --vr-error              --window で SteamVR へつなげない状態の段を描く
              --dashboard             SteamVRのダッシュボードの設定の画面を描く
              --reset-warning         履歴リセットの予告のアイコンを描く
              --welcome               初回起動の案内の画面を描く
              --setup                 --welcome で「わかった」のあとの初期設定の画面を描く
              --scroll <px>           パネルのスクロール位置（負なら末尾）
              --mark-popup            「行を指して目印を選ぶ」状態を描く
              --reset-confirm         見出しの「リセット」の確認を描く
            """);
    }
}
