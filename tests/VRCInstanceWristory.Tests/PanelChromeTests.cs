using VRCInstanceWristory.Core;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 見出しと下部の文字（2026-09-19のユーザー指定）。
/// 件数は下部右から見出し左へ移し、下部右にはアプリ名とバージョンを出す。
/// 下部左は空にする（後で何か入れるかもしれないので場所は残す）。
/// </summary>
public class PanelChromeTests
{
    private static readonly PanelStyle Style = new();

    [Fact]
    public void 見出しは件数を含む()
    {
        Assert.Equal("訪問履歴 (全0件)", PanelRenderer.HeaderTitle(0));
        Assert.Equal("訪問履歴 (全9件)", PanelRenderer.HeaderTitle(9));
        Assert.Equal("訪問履歴 (全123件)", PanelRenderer.HeaderTitle(123));
    }

    /// <summary>
    /// 件数の後ろに、先頭の行（いちばん古い訪問）へ入った時刻を出す（2026-09-25のユーザー指定）。
    /// 履歴がいつからのものかが、スクロールして先頭を見に行かなくても分かる。
    /// </summary>
    [Fact]
    public void 見出しは先頭の行へ入った時刻を含む()
    {
        Assert.Equal("訪問履歴 (全8件 00:14~)", PanelRenderer.HeaderTitle(8, "00:14"));

        // 行がなければ時刻は出さない。
        Assert.Equal("訪問履歴 (全0件)", PanelRenderer.HeaderTitle(0, null));

        // 履歴の自動リセットを無効にしている間は件数を出さない（→実装メモ5.109）。
        Assert.Equal("訪問履歴 (2026-09-27 16:24~)", PanelRenderer.HeaderTitle(8, "2026-09-27 16:24", showCount: false));
        Assert.Equal("訪問履歴", PanelRenderer.HeaderTitle(0, null, showCount: false));
    }

    /// <summary>描いた見出しの時刻は、スクロールの位置によらず先頭の行のもの。</summary>
    [Fact]
    public void 見出しの時刻は先頭の行の入室時刻になる()
    {
        using var renderer = new PanelRenderer(Style);

        var rows = SampleRows.Build();
        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, scrollOffset: 200f);

        Assert.Equal($"訪問履歴 (全{rows.Count}件 {rows[0].JoinText}~)", renderer.HeaderTitleText);
        Assert.Equal(5, rows[0].JoinText.Length);
    }

    [Fact]
    public void 下部右はアプリ名とバージョン_左は空()
    {
        Assert.Equal("VRC Instance Wristory", AppInfo.DisplayName);
        Assert.StartsWith("v", AppInfo.ShortVersion);
        Assert.Equal($"{AppInfo.DisplayName} {AppInfo.ShortVersion}", AppInfo.NameWithVersion);
    }

    /// <summary>
    /// 実際に描いたときも、下部の左半分には何も出ず、右側にだけ文字が出る。
    /// </summary>
    [Fact]
    public void 下部の左半分には何も描かない()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = renderer.Measure(SampleRows.Build());
        renderer.Render(layouts, 0f);

        var pixels = renderer.GetPixels();
        var top = Style.MaxHeight - Style.FooterHeight + 2;
        var bottom = Style.MaxHeight - 2;

        // 下部の帯は一様に背景の不透明度。文字があればその画素だけ不透明度が上がる。
        static bool HasText(byte[] pixels, PanelStyle style, int x0, int x1, int y0, int y1)
        {
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    if (pixels[((y * style.Width) + x) * 4 + 3] > style.BackgroundAlpha)
                        return true;
                }
            }

            return false;
        }

        Assert.False(HasText(pixels, Style, 0, Style.Width / 2, top, bottom), "下部左は空のはず");
        Assert.True(HasText(pixels, Style, Style.Width / 2, Style.Width, top, bottom), "下部右にアプリ名が出るはず");
    }
}
