using VRCInstanceWristory.Core.Locations;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// 利用者が付けたグループ名（<see cref="AppSettings.GroupNames"/>）を変える（→実装メモ5.48・5.65）。
/// 設定ファイルへの書き戻しは呼び出し側（主ループ）が行う。
/// </summary>
public static class GroupNameEditor
{
    /// <summary>取り込んだ結果。<see cref="Unchanged"/> は、変わらなかったものと使えなかったものの数。</summary>
    public readonly record struct ImportResult(int Added, int Replaced, int Unchanged)
    {
        public bool Changed => Added + Replaced > 0;

        /// <summary>利用者へ知らせる文。</summary>
        public string Summary => $"グループ名を{Added + Replaced}件インポートしました（新規{Added}件・上書き{Replaced}件・変更なし{Unchanged}件）。";
    }

    /// <summary>
    /// ファイルから読んだグループ名を取り込む（2026-09-27のユーザー指定→実装メモ5.65）。
    /// 同じ Group ID の名前は読んだほうで置き換え、ファイルにないグループの名前は残す。
    /// </summary>
    public static ImportResult Import(AppSettings settings, IReadOnlyDictionary<string, string> names)
    {
        var added = 0;
        var replaced = 0;

        foreach (var (groupId, name) in names)
        {
            // ウィンドウの側で検めてあるが、設定へ入れる前にもう一度同じ決まりで確かめる。
            if (!LocationParser.IsValidGroupId(groupId) || AppSettings.NormalizeGroupName(name) is not { } normalized)
                continue;

            if (!settings.GroupNames.TryGetValue(groupId, out var current))
                added++;
            else if (current != normalized)
                replaced++;
            else
                continue;

            settings.GroupNames[groupId] = normalized;
        }

        return new ImportResult(added, replaced, names.Count - added - replaced);
    }

    /// <summary>
    /// グループに名前を付ける・外す（2026-09-26のユーザー指定→実装メモ5.48）。空の名前なら外す。
    /// 変わったときだけ true。使えない名前なら理由を出して false。
    /// </summary>
    public static bool Set(AppSettings settings, string groupId, string? name, IDiagnostics log)
    {
        if (!LocationParser.IsValidGroupId(groupId))
            return false;

        var normalized = AppSettings.NormalizeGroupName(name);

        if (!string.IsNullOrWhiteSpace(name) && normalized is null)
        {
            log.Warn($"グループ名は{AppSettings.MaxGroupNameLength}文字までで、改行などは使えません。");
            return false;
        }

        if (settings.GroupNames.GetValueOrDefault(groupId) == normalized)
            return false;

        if (normalized is null)
            settings.GroupNames.Remove(groupId);
        else
            settings.GroupNames[groupId] = normalized;

        return true;
    }

    /// <summary>付けてあるグループ名をファイルへ書き出す（2026-09-27のユーザー指定→実装メモ5.65）。利用者へ知らせる文と、書けたか。</summary>
    public static (string Text, bool Ok) Export(IReadOnlyDictionary<string, string> names, string path, IDiagnostics log)
    {
        try
        {
            GroupNameFile.Write(path, names);
            var text = $"グループ名を{names.Count}件エクスポートしました。\n{path}";
            log.Notice(text.Replace('\n', ' '));
            return (text, true);
        }
        catch (Exception ex)
        {
            log.Error($"グループ名を書き出せません: {ex.Message}");
            return ($"グループ名を書き出せません: {ex.Message}", false);
        }
    }
}
