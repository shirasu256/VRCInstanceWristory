namespace VRCInstanceWristory.Core.Scrolling;

/// <param name="EventId">行の識別子。期限切れ後の位置合わせに使う。</param>
/// <param name="Height">行の高さ（px）。長いIDを折り返した行は高くなる。</param>
public readonly record struct ScrollRow(string EventId, float Height);

/// <summary>
/// 一覧のスクロール位置（仕様8.2・8.3節）。
///
/// - 上端を0とし、0 &lt;= offset &lt;= max(0, contentHeight - viewportHeight) でクランプする。
/// - 上へ倒すと古い側（offsetが減る）、下へ倒すと新しい側（offsetが増える）。
/// - 速度は倒し量に比例し、時間差で積分するのでフレームレートに依存しない。
/// - 非表示・非命中・追跡消失中は積分しない。v0.1に慣性はない。
/// </summary>
public sealed class ScrollController
{
    public const float DefaultRowHeight = 112f;

    public const int VisibleRows = 7;

    private List<ScrollRow> _rows = [];
    private List<float> _tops = [];
    private float _contentHeight;
    private bool _requireNeutral;

    /// <summary>標準行の高さ。速度の基準に使う。</summary>
    public float RowHeight { get; set; } = DefaultRowHeight;

    public float ViewportHeight { get; set; } = DefaultRowHeight * VisibleRows;

    /// <summary>軸の絶対値がこれ以下なら中立。</summary>
    public float Deadzone { get; set; } = 0.20f;

    /// <summary>最大速度（1秒あたりの標準行数）。</summary>
    public float RowsPerSecond { get; set; } = 6f;

    public float Offset { get; private set; }

    /// <summary>末尾を見ているか。真のときだけ新着に追従する。</summary>
    public bool FollowTail { get; private set; } = true;

    public float ContentHeight => _contentHeight;

    public float MaxOffset => Math.Max(0f, _contentHeight - ViewportHeight);

    public int RowCount => _rows.Count;

    /// <summary>現在見えている先頭行の番号（0始まり）。</summary>
    public int TopRowIndex
    {
        get
        {
            for (var i = _rows.Count - 1; i >= 0; i--)
            {
                if (_tops[i] <= Offset + 0.5f)
                    return i;
            }

            return 0;
        }
    }

    /// <summary>高さの等しい行として設定する（検証用の簡易版）。</summary>
    public void SetRows(IReadOnlyList<string> eventIds)
        => SetRows(eventIds.Select(id => new ScrollRow(id, RowHeight)).ToList());

    /// <summary>
    /// 表示対象の行を更新する。末尾追従中なら末尾へ、そうでなければ見えている先頭行を保つ。
    /// 基準行が期限切れで消えた場合は直後の残存行に合わせ、最後に範囲内へクランプする。
    /// </summary>
    public void SetRows(IReadOnlyList<ScrollRow> rows)
    {
        var previousRows = _rows;
        var previousTops = _tops;
        var previousOffset = Offset;
        var followTail = FollowTail;

        _rows = [.. rows];
        _tops = new List<float>(_rows.Count);

        var y = 0f;
        foreach (var row in _rows)
        {
            _tops.Add(y);
            y += row.Height;
        }

        _contentHeight = y;

        if (followTail)
        {
            Offset = MaxOffset;
            FollowTail = true;
            return;
        }

        if (previousRows.Count == 0)
        {
            Offset = Clamp(previousOffset);
            UpdateFollowTail();
            return;
        }

        var topIndex = 0;
        for (var i = previousRows.Count - 1; i >= 0; i--)
        {
            if (previousTops[i] <= previousOffset + 0.5f)
            {
                topIndex = i;
                break;
            }
        }

        var intra = previousOffset - previousTops[topIndex];

        var newIndex = -1;
        for (var i = topIndex; i < previousRows.Count && newIndex < 0; i++)
            newIndex = IndexOf(_rows, previousRows[i].EventId);

        if (newIndex < 0)
        {
            Offset = Clamp(previousOffset);
            UpdateFollowTail();
            return;
        }

        Offset = Clamp(_tops[newIndex] + intra);
        UpdateFollowTail();
    }

    /// <summary>スティック入力を反映する。</summary>
    /// <param name="y">上を正とした軸の値。</param>
    /// <param name="active">
    /// showPanel かつ 右手pose有効 かつ UI命中 かつ scroll actionが有効な間だけ true。
    /// </param>
    /// <param name="deltaSeconds">前回からの経過時間。非活性の間は積分しない。</param>
    public void Update(float y, bool active, float deltaSeconds)
    {
        if (!active)
        {
            // 命中が外れたらすぐ停止する。次に命中したときは中立から始める。
            _requireNeutral = true;
            return;
        }

        var magnitude = Math.Abs(y);

        if (_requireNeutral)
        {
            // 命中開始時に倒れていた場合は、一度ニュートラルへ戻るまで受け付けない。
            if (magnitude > Deadzone)
                return;

            _requireNeutral = false;
        }

        if (magnitude <= Deadzone || deltaSeconds <= 0f)
            return;

        var normalized = Math.Clamp((magnitude - Deadzone) / Math.Max(0.0001f, 1f - Deadzone), 0f, 1f);
        var speed = normalized * RowHeight * RowsPerSecond; // px/秒
        var delta = -Math.Sign(y) * speed * deltaSeconds;

        Offset = Clamp(Offset + delta);
        UpdateFollowTail();
    }

    /// <summary>
    /// 決まった量だけ動かす（デスクトップのウィンドウのマウスホイール→実装メモ5.39）。
    /// 正で新しい側（offsetが増える）。スティックと違って中立を待たない。
    /// </summary>
    public void ScrollBy(float pixels)
    {
        if (!float.IsFinite(pixels))
            return;

        Offset = Clamp(Offset + pixels);
        UpdateFollowTail();
    }

    /// <summary>初回表示・対象外を経ての復帰。最新側（末尾）から始める。</summary>
    public void ResetToTail()
    {
        Offset = MaxOffset;
        FollowTail = true;
        _requireNeutral = true;
    }

    /// <summary>行の上端位置（内容座標）。</summary>
    public float RowTop(int index) => index >= 0 && index < _tops.Count ? _tops[index] : 0f;

    private void UpdateFollowTail() => FollowTail = Offset >= MaxOffset - 0.5f;

    private float Clamp(float value) => Math.Clamp(value, 0f, MaxOffset);

    private static int IndexOf(List<ScrollRow> rows, string id)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].EventId, id, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
