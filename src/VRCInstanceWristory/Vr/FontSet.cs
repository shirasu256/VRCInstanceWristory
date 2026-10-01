using System.Drawing;
using System.Drawing.Text;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// 描画に使うフォント。IDは 0/O・1/l/I を読み分けやすい等幅、日本語は対応フォントへ落とす
/// （仕様8.1節）。存在しないフォント名は順に読み飛ばす。
/// </summary>
public sealed class FontSet : IDisposable
{
    private static readonly string[] MonospaceCandidates =
        ["Cascadia Mono", "Consolas", "Lucida Console", "DejaVu Sans Mono"];

    private static readonly string[] JapaneseCandidates =
        ["Yu Gothic UI", "Meiryo UI", "Yu Gothic", "Meiryo", "MS UI Gothic", "MS Gothic"];

    /// <summary>
    /// 使うフォントの名前（等幅・日本語）。入っているフォントの一覧を引くのは重いので、プロセスの中で1回だけにする
    /// （SteamVR へつなぎ直すたびにパネルを作り直すため）。
    /// </summary>
    private static readonly Lazy<(string Monospace, string Japanese)> FamilyNames = new(ResolveFamilyNames);

    private readonly List<FontFamily> _owned = [];

    public FontSet(PanelStyle style)
        : this(style.TitleFontSize, style.AuxFontSize, style.CountdownFontSize, style.ResetButtonFontSize, style.AbsenceFontSize, style.IdFontSize)
    {
    }

    /// <summary>字の大きさ（画素）を直接渡して作る。パネルの配色などは要らない描き手（デスクトップのウィンドウ）が使う。</summary>
    public FontSet(float titleSize, float auxSize, float countdownSize, float resetLabelSize, float absenceSize, float idSize)
    {
        (MonospaceFamilyName, JapaneseFamilyName) = FamilyNames.Value;

        var mono = CreateFamily(MonospaceFamilyName, GenericFontFamilies.Monospace);
        var jp = CreateFamily(JapaneseFamilyName, GenericFontFamilies.SansSerif);

        Id = new Font(mono, idSize, FontStyle.Regular, GraphicsUnit.Pixel);
        IdSmall = new Font(mono, idSize * 0.72f, FontStyle.Regular, GraphicsUnit.Pixel);
        Aux = new Font(jp, auxSize, FontStyle.Regular, GraphicsUnit.Pixel);
        AuxMono = new Font(mono, auxSize, FontStyle.Regular, GraphicsUnit.Pixel);
        Countdown = new Font(mono, countdownSize, FontStyle.Regular, GraphicsUnit.Pixel);
        ResetLabel = new Font(jp, resetLabelSize, FontStyle.Regular, GraphicsUnit.Pixel);
        Absence = new Font(jp, absenceSize, FontStyle.Regular, GraphicsUnit.Pixel);
        Title = new Font(jp, titleSize, FontStyle.Regular, GraphicsUnit.Pixel);
        Marker = new Font(jp, idSize * 0.6f, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    public string MonospaceFamilyName { get; }

    public string JapaneseFamilyName { get; }

    /// <summary>IDの標準サイズ。</summary>
    public Font Id { get; }

    /// <summary>長いIDを1行に収めるための縮小サイズ。</summary>
    public Font IdSmall { get; }

    /// <summary>付加情報（日本語を含む）。</summary>
    public Font Aux { get; }

    /// <summary>時刻・短縮Group IDなど、桁の揃った付加情報。</summary>
    public Font AuxMono { get; }

    /// <summary>見出し右のカウントダウン。桁が揺れないよう等幅。</summary>
    public Font Countdown { get; }

    /// <summary>見出しのボタン（「延長」「リセット」）の文字。</summary>
    public Font ResetLabel { get; }

    /// <summary>行の間に入れる「∧ 対象外のインスタンスへ移動 ∨」の文字。</summary>
    public Font Absence { get; }

    public Font Title { get; }

    public Font Marker { get; }

    /// <summary>名前のフォントを作る。作れなければ汎用のフォントにする。どちらもこの組が持ち、<see cref="Dispose"/> で手放す。</summary>
    private FontFamily CreateFamily(string name, GenericFontFamilies fallback)
    {
        FontFamily family;

        try
        {
            family = new FontFamily(name);
        }
        catch (ArgumentException)
        {
            family = new FontFamily(fallback);
        }

        _owned.Add(family);
        return family;
    }

    /// <summary>入っているフォントの一覧を1回だけ引いて、等幅と日本語の候補からそれぞれ最初に見つかったものを選ぶ。</summary>
    private static (string Monospace, string Japanese) ResolveFamilyNames()
    {
        HashSet<string> installed;

        using (var collection = new InstalledFontCollection())
        {
            // Families は呼ぶたびに FontFamily を作って返すので、名前を取ったら手放す。
            var families = collection.Families;

            try
            {
                installed = families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                foreach (var family in families)
                    family.Dispose();
            }
        }

        return (Pick(MonospaceCandidates, installed, GenericFontFamilies.Monospace), Pick(JapaneseCandidates, installed, GenericFontFamilies.SansSerif));
    }

    private static string Pick(string[] candidates, HashSet<string> installed, GenericFontFamilies fallback)
    {
        foreach (var candidate in candidates)
        {
            if (installed.Contains(candidate))
                return candidate;
        }

        using var generic = new FontFamily(fallback);
        return generic.Name;
    }

    public void Dispose()
    {
        Id.Dispose();
        IdSmall.Dispose();
        Aux.Dispose();
        AuxMono.Dispose();
        Countdown.Dispose();
        ResetLabel.Dispose();
        Absence.Dispose();
        Title.Dispose();
        Marker.Dispose();

        foreach (var family in _owned)
            family.Dispose();

        _owned.Clear();
    }
}
