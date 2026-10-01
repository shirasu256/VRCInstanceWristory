using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// 撮った写真の小さなサムネイル（2026-09-27のユーザー指定→実装メモ5.55）。
///
/// 写真はログの <c>Took screenshot to:</c> から分かる（→5.47）。その写真を読んで縮めた JPEG を
/// <c>%LocalAppData%\VRCInstanceWristory\thumbnails</c> に置き、デスクトップのウィンドウの「選んだ行」に並べる。
///
/// <list type="bullet">
/// <item>作るのは専用のスレッド（優先度を下げたもの）。VRChat の写真は 7680×4320 の PNG もあり、読むのに1秒前後かかるので、
/// 主ループ（VRの描画）とウィンドウのスレッドを止めない</item>
/// <item>主ループは、いまの履歴にある写真の一覧を <see cref="Sync"/> で渡すだけ。一覧から消えた写真（ログのリセットで
/// 行と一緒に消えた訪問のもの）のサムネイルは、そのときに消す</item>
/// <item>できたら <see cref="Ready"/> で知らせる（作ったスレッドから呼ぶ）。ウィンドウは <see cref="TryLoad"/> で読み直す</item>
/// </list>
///
/// ファイル名は写真の場所から作る（大文字小文字を区別しない SHA-256 の先頭）。写真そのものは複写しない。
/// </summary>
public sealed class PhotoThumbnails : IDisposable
{
    /// <summary>サムネイルの最大の大きさ（画素）。ウィンドウでの表示（約100×56の論理px）を 300% まで粗くせずに出せる。</summary>
    public const int MaxWidth = 320;

    public const int MaxHeight = 180;

    /// <summary>読めなかった写真を試し直すまでの間（回数ごと）。書き終わる前に読んだとき・一時的に開けないときのため。</summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];

    private const string Extension = ".jpg";

    private readonly string _directory;
    private readonly IDiagnostics _log;
    private readonly bool _generate;
    private readonly Func<string, Image?> _decode;
    private readonly Lock _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;

    // 主ループから渡された最新の一覧（作るスレッドが取り込む）。
    private IReadOnlyList<string>? _pendingWanted;
    private volatile bool _stopping;

    // 作るスレッドだけが触る。
    private List<string> _wanted = [];
    private readonly Dictionary<string, (int Attempts, DateTime RetryAtUtc)> _failures = new(StringComparer.OrdinalIgnoreCase);

    // 主ループだけが触る（同じ一覧を何度も渡さない）。
    private HashSet<string>? _lastSent;

    /// <param name="generate">
    /// false なら作らず、消すだけにする（デスクトップのウィンドウを出していないとき。見せる場所がないので作る意味がない）。
    /// </param>
    /// <param name="decode">写真を読む処理（検証で差し替える）。既定はファイルから読む。</param>
    public PhotoThumbnails(string directory, IDiagnostics log, bool generate = true, Func<string, Image?>? decode = null)
    {
        _directory = directory;
        _log = log;
        _generate = generate;
        _decode = decode ?? DecodeFile;
        _worker = new Thread(Run)
        {
            Name = "PhotoThumbnails",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };

        _worker.Start();
    }

    /// <summary>サムネイルができた（引数は写真の場所）。作るスレッドから呼ぶ。</summary>
    public event Action<string>? Ready;

    /// <summary>写真の場所から、サムネイルのファイル名を作る。</summary>
    public static string FileName(string photoPath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(photoPath.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant() + Extension;
    }

    /// <summary>その写真のサムネイルの場所（まだないこともある）。</summary>
    public string PathFor(string photoPath) => Path.Combine(_directory, FileName(photoPath));

    /// <summary>
    /// いまの履歴にある写真（新しい順がよい。先に作る）を渡す。主ループから呼ぶ。
    /// 前と同じ一覧なら何もしない。一覧にないサムネイルは消し、足りないものを作る。
    /// </summary>
    public void Sync(IReadOnlyList<string> photos)
    {
        var set = new HashSet<string>(photos, StringComparer.OrdinalIgnoreCase);

        if (_lastSent is not null && _lastSent.SetEquals(set))
            return;

        _lastSent = set;

        lock (_gate)
            _pendingWanted = [.. photos];

        _wake.Set();
    }

    /// <summary>
    /// サムネイルを読む（ウィンドウのスレッドから）。まだなければ null。
    /// ファイルを掴んだままにしないよう、読んだ中身を写した絵を返す（呼び出し側が捨てる）。
    /// </summary>
    public Bitmap? TryLoad(string photoPath)
    {
        var path = PathFor(photoPath);

        try
        {
            if (!File.Exists(path))
                return null;

            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch (Exception)
        {
            // 作り直している途中などで読めなかった。次に描くときに試し直す。
            return null;
        }
    }

    /// <summary>作るスレッドが手すきになるまで待つ（検証用）。</summary>
    internal bool WaitIdle(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < until)
        {
            lock (_gate)
            {
                if (_pendingWanted is null && _idle)
                    return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private bool _idle = true;

    // ------------------------------------------------------------------ 作るスレッド

    private void Run()
    {
        while (!_stopping)
        {
            var wait = NextRetryDelay();
            _wake.WaitOne(wait);

            if (_stopping)
                break;

            lock (_gate)
            {
                if (_pendingWanted is { } wanted)
                {
                    _wanted = [.. wanted];
                    _pendingWanted = null;
                }

                _idle = false;
            }

            try
            {
                RemoveStale();

                if (_generate)
                    GenerateMissing();
            }
            catch (Exception ex)
            {
                _log.Error($"写真のサムネイルでエラー: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                lock (_gate)
                    _idle = true;
            }
        }
    }

    /// <summary>次に試し直す写真があれば、その時刻までの間。なければ待ち続ける。</summary>
    private TimeSpan NextRetryDelay()
    {
        var now = DateTime.UtcNow;
        var waiting = _failures.Values.Where(f => f.Attempts <= RetryDelays.Length).Select(f => f.RetryAtUtc).ToList();

        if (!_generate || waiting.Count == 0)
            return Timeout.InfiniteTimeSpan;

        var next = waiting.Min() - now;
        return next < TimeSpan.Zero ? TimeSpan.Zero : next;
    }

    /// <summary>いまの一覧にない写真のサムネイル（と、作りかけの一時ファイル）を消す。</summary>
    private void RemoveStale()
    {
        if (!Directory.Exists(_directory))
            return;

        var keep = new HashSet<string>(_wanted.Select(FileName), StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(_directory))
        {
            var name = Path.GetFileName(file);

            if (keep.Contains(name))
                continue;

            if (!name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(file);
                removed++;
            }
            catch (IOException)
            {
                // ウィンドウが読んでいる途中など。次の一覧で消す。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (var stale in _failures.Keys.Where(k => !_wanted.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            _failures.Remove(stale);

        if (removed > 0)
            _log.Info($"履歴から消えた写真のサムネイル{removed}件を消しました。");
    }

    private void GenerateMissing()
    {
        foreach (var photo in _wanted)
        {
            if (_stopping)
                return;

            // 新しい一覧が届いたら、そちらを先に反映する（ログのリセットで消すのを待たせない）。
            lock (_gate)
            {
                if (_pendingWanted is not null)
                {
                    _wake.Set();
                    return;
                }
            }

            var target = PathFor(photo);

            if (File.Exists(target))
                continue;

            if (_failures.TryGetValue(photo, out var failure) && (failure.Attempts > RetryDelays.Length || DateTime.UtcNow < failure.RetryAtUtc))
                continue;

            if (TryGenerate(photo, target, out var error))
            {
                _failures.Remove(photo);
                Ready?.Invoke(photo);
                continue;
            }

            var attempts = failure.Attempts + 1;
            _failures[photo] = (attempts, DateTime.UtcNow + RetryDelays[Math.Min(attempts, RetryDelays.Length) - 1]);

            if (attempts > RetryDelays.Length)
                _log.Warn($"写真のサムネイルを作れません（{error}）: {photo}");
        }
    }

    private bool TryGenerate(string photo, string target, out string error)
    {
        error = string.Empty;

        try
        {
            using var source = _decode(photo);

            if (source is null)
            {
                error = "写真が見つかりません";
                return false;
            }

            var (width, height) = FitSize(source.Width, source.Height, MaxWidth, MaxHeight);

            using var thumbnail = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            using (var graphics = Graphics.FromImage(thumbnail))
            {
                // 背景が透明な写真もあるので、暗い色の上に描く。
                graphics.Clear(Color.FromArgb(0x1c, 0x24, 0x2b));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));
            }

            Directory.CreateDirectory(_directory);
            var temp = target + ".tmp";
            SaveJpeg(thumbnail, temp);
            File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            // GDI+ は壊れた（書きかけの）画像に OutOfMemoryException・ArgumentException を投げる。
            error = ex.Message;
            return false;
        }
    }

    /// <summary>縦横比を保って、最大の大きさに収まる大きさ（1画素より小さくはしない）。</summary>
    public static (int Width, int Height) FitSize(int width, int height, int maxWidth, int maxHeight)
    {
        if (width <= 0 || height <= 0)
            return (1, 1);

        var scale = Math.Min(1.0, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static Image? DecodeFile(string photo)
    {
        if (!File.Exists(photo))
            return null;

        // 中身を先に読み切ってから絵にする。ファイルを掴んだままにしない（VRChat や利用者が移動・削除できるように）。
        // 絵は読んだ中身（MemoryStream）を参照し続けるので、ストリームはここで閉じない（絵と一緒に捨てられる）。
        byte[] bytes;

        using (var stream = new FileStream(photo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }

        return Image.FromStream(new MemoryStream(bytes, writable: false), useEmbeddedColorManagement: false, validateImageData: true);
    }

    private static void SaveJpeg(Bitmap bitmap, string path)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
        bitmap.Save(path, codec, parameters);
    }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        _worker.Join(TimeSpan.FromSeconds(3));
        _wake.Dispose();
    }
}
