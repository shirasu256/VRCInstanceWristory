using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using VRCInstanceWristory.Core.Locations;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// グループ名の書き出しと読み込み（2026-09-27のユーザー指定→実装メモ5.65）。
///
/// 書き出す形は設定ファイルの <c>groupNames</c> と同じで、<c>{ "groupNames": { "grp_…": "名前" } }</c>。
/// 読むときは、この形のほか、名前を並べただけの <c>{ "grp_…": "名前" }</c> と、<c>settings.json</c> そのものも受け付ける。
/// 名前は設定ファイルと同じ決まり（grp_ で始まる Group ID と、<see cref="AppSettings.MaxGroupNameLength"/>文字までの名前）で検め、
/// 使えないものは飛ばして数だけを返す。
/// </summary>
public static class GroupNameFile
{
    /// <summary>ファイルの中で名前を並べるキー（設定ファイルと同じ）。</summary>
    public const string Key = "groupNames";

    /// <summary>ファイルを選ぶ画面に出す既定の名前。</summary>
    public const string DefaultFileName = $"{AppInfo.InternalName}-groupNames.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>読んだ結果。<see cref="Skipped"/> は使えなかった項目の数。</summary>
    public sealed record ReadResult(IReadOnlyDictionary<string, string> Names, int Skipped);

    /// <summary>グループ名を書き出す（Group ID の順）。書けなければ例外を投げる。</summary>
    public static void Write(string path, IReadOnlyDictionary<string, string> names)
        => AtomicFile.WriteAllText(path, Serialize(names));

    public static string Serialize(IReadOnlyDictionary<string, string> names)
    {
        var list = new JsonObject();

        foreach (var (groupId, name) in names.OrderBy(p => p.Key, StringComparer.Ordinal))
            list[groupId] = JsonValue.Create(name);

        return new JsonObject { [Key] = list }.ToJsonString(Options);
    }

    /// <summary>ファイルを読む。JSON として読めない・形が違うときは null と、その理由。</summary>
    public static ReadResult? Read(string path, out string error)
    {
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            error = $"ファイルを読めません: {ex.Message}";
            return null;
        }

        return Parse(text, out error);
    }

    public static ReadResult? Parse(string text, out string error)
    {
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(text, documentOptions: ReadOptions);
        }
        catch (JsonException ex)
        {
            error = $"JSON として読めません: {ex.Message}";
            return null;
        }

        if (root is not JsonObject top)
        {
            error = "グループ名のファイルではありません（{ \"groupNames\": { \"grp_…\": \"名前\" } } の形で書きます）。";
            return null;
        }

        // { "groupNames": {…} }（書き出した形・settings.json）か、名前を並べただけの形。
        var list = top.TryGetPropertyValue(Key, out var inner) ? inner as JsonObject : top;

        if (list is null)
        {
            error = $"「{Key}」がオブジェクトではありません。";
            return null;
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = 0;

        foreach (var (groupId, value) in list)
        {
            // 名前を並べただけの形では、settings.json のほかのキーも混ざりうる。grp_ で始まらないものは数えずに飛ばす。
            if (!ReferenceEquals(list, top) || groupId.StartsWith(LocationParser.GroupPrefix, StringComparison.Ordinal))
            {
                var name = value is JsonValue v && v.TryGetValue<string>(out var s) ? AppSettings.NormalizeGroupName(s) : null;

                if (LocationParser.IsValidGroupId(groupId) && name is not null)
                    names[groupId] = name;
                else
                    skipped++;
            }
        }

        if (names.Count == 0 && skipped == 0)
        {
            error = "グループ名が1つも書かれていません。";
            return null;
        }

        error = string.Empty;
        return new ReadResult(names, skipped);
    }
}
