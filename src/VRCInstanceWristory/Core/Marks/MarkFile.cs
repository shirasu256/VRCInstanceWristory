using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VRCInstanceWristory.Core.Marks;

/// <summary>
/// 目印の保存先（<c>%LocalAppData%\VRCInstanceWristory\marks.json</c>・→実装メモ5.44・5.54）。
///
/// 中身はインスタンスごと（鍵は locationKey）の目印と、付けた時刻。
/// <code>
/// { "schemaVersion": 2, "instances": { "wrld_…:07254~…": { "mark": "Heart", "markedAtUtc": "2026-09-27T01:23:45Z" } } }
/// </code>
/// 形式1（2026-09-26の版。目印の名前だけで、ワールドごとの欄もあった）も読む。付けた時刻はファイルを書いた時刻とし、
/// ワールドごとの欄は読まない（ワールドごとに残す設定はやめた→5.54）。
///
/// 保存は一時ファイルからの置換で行い、途中で落ちても壊れた中身を残さない（<see cref="Counting.CheckpointStore"/> と同じ）。
/// </summary>
public sealed class MarkFile(string path)
{
    public const int CurrentSchemaVersion = 2;

    // 日本語の名前などをエスケープせずに書く（CheckpointStore と同じ）。
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    public string Path { get; } = path;

    public string? LastError { get; private set; }

    /// <summary>読めない・壊れている・まだないときは空の中身を返す（理由は <see cref="LastError"/>）。</summary>
    public Dictionary<string, MarkEntry> Load()
    {
        LastError = null;
        var result = new Dictionary<string, MarkEntry>(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(Path))
                return result;

            var root = JsonNode.Parse(File.ReadAllText(Path)) as JsonObject;
            var version = root?["schemaVersion"]?.GetValue<int>() ?? 0;

            if (root is null || version is < 1 or > CurrentSchemaVersion)
            {
                LastError = root is null ? "marks.json is empty" : $"unsupported schemaVersion {version}";
                return result;
            }

            var written = File.GetLastWriteTimeUtc(Path);

            foreach (var (key, value) in root["instances"] as JsonObject ?? [])
            {
                if (string.IsNullOrWhiteSpace(key) || value is null)
                    continue;

                // 形式1は名前だけ、形式2は名前と付けた時刻。
                var (name, markedAt) = value is JsonObject entry
                    ? (entry["mark"]?.GetValue<string>(), ParseTime(entry["markedAtUtc"]?.GetValue<string>()) ?? written)
                    : (value.GetValue<string>(), written);

                // 目印なし（None）と知らない名前は落とす。
                if (Enum.TryParse<InstanceMark>(name, ignoreCase: true, out var mark) && mark != InstanceMark.None && Enum.IsDefined(mark))
                    result[key] = new MarkEntry(mark, markedAt);
            }

            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new Dictionary<string, MarkEntry>(StringComparer.Ordinal);
        }
    }

    public bool Save(IReadOnlyDictionary<string, MarkEntry> marks)
    {
        LastError = null;

        try
        {
            var instances = new JsonObject();

            foreach (var (key, entry) in marks.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(key) || entry.Mark == InstanceMark.None)
                    continue;

                instances[key] = new JsonObject
                {
                    ["mark"] = entry.Mark.ToString(),
                    ["markedAtUtc"] = DateTime.SpecifyKind(entry.MarkedAtUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
                };
            }

            var root = new JsonObject
            {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["instances"] = instances,
            };

            Infrastructure.AtomicFile.Write(Path, stream =>
            {
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = Encoder });
                root.WriteTo(writer);
            });

            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static DateTime? ParseTime(string? text)
        => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
            ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
            : null;
}
