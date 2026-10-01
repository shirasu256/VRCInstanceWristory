using VRCInstanceWristory.Core;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// AFK の受け口（→実装メモ5.89）を開いたあと、Windows のファイアウォールでブロックされていないかを確かめる（2026-10-01のユーザー指定→実装メモ5.111・5.115）。
///
/// Windows は許可を求める画面を出すと同時にブロックの規則を作り、「許可」を押すとそれを書き換える（→5.112）。
/// 画面が出ている間の規則は「許可しない」を選んだあとと中身が同じなので、規則だけでは答え待ちかどうか分からない。
/// そこで、<b>このアプリのウィンドウが手前にあるときだけ</b>読む。画面が出ている間は画面のほうが手前にあり、
/// 答えると、手前にあったこのアプリのウィンドウへ戻る。画面をゆっくり読んでいても、答えるまでは決めない。
///
/// | 規則（このアプリが手前のとき） | 動作 |
/// | --- | --- |
/// | ブロック | <see cref="Interval"/> おいて<b>2回続けて</b>ブロックなら、<see cref="Poll"/> が1回だけ true を返して見張りを終える。呼び出し側が検知をオフに戻す |
/// | まだない | 許可を求める画面がこれから出るかもしれないので、<see cref="Interval"/> ごとに読み直す（手前で読んだ回数で <see cref="WatchFor"/> ぶんまで） |
/// | 許可・読めない | 見張りを終える（読めないときは、これまでどおり開いたままにする） |
///
/// このアプリが手前にない間は読まず、ブロックの数を数え直す（ダッシュボードから押したときは、ウィンドウへ戻ってから決まる）。
/// 2回続けて確かめるのは、「許可」で画面が閉じてから規則が書き換わるまでの一瞬を読んでも決めないため。
/// 規則を読むのに 0.3〜0.4 秒かかるので、主ループ（VR の描画）を止めないよう別のスレッドで読む。
/// </summary>
/// <param name="inFront">このアプリのウィンドウが手前にあるか。ウィンドウを出していなければ、いつも true を返すものを渡す。</param>
public sealed class AfkFirewallWatch(Func<FirewallVerdict> check, Func<bool> inFront, IClock clock, Func<Func<FirewallVerdict>, Task<FirewallVerdict>>? run = null)
{
    /// <summary>読み直す間隔。</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>規則がまだないまま読み続ける長さ（手前で読んだ回数×<see cref="Interval"/>）。これを超えたら見張りを終える。</summary>
    public static readonly TimeSpan WatchFor = TimeSpan.FromMinutes(5);

    private readonly Func<Func<FirewallVerdict>, Task<FirewallVerdict>> _run = run ?? (c => Task.Run(c));

    private bool _watching;
    private TimeSpan _noRuleFor;
    private DateTime _nextUtc;
    private Task<FirewallVerdict>? _pending;
    private int _generation;
    private int _pendingGeneration;
    private bool _pendingInFront;
    private int _blockedReads;

    /// <summary>見張っているか。</summary>
    public bool Watching => _watching;

    /// <summary>受け口を開いた。このアプリが手前にあればすぐに読む。</summary>
    public void Start()
    {
        _generation++;
        _watching = true;
        _noRuleFor = TimeSpan.Zero;
        _nextUtc = clock.UtcNow;
        _blockedReads = 0;
    }

    /// <summary>受け口を閉じた。読んでいる途中の結果は捨てる。</summary>
    public void Stop()
    {
        _generation++;
        _watching = false;
    }

    /// <summary>主ループから毎回呼ぶ。ブロックされていると分かったときだけ true（1回だけ）。</summary>
    public bool Poll()
    {
        var front = inFront();

        // 読んでいる途中で手前でなくなった（許可を求める画面が出たかもしれない）結果は使わない。
        if (_pending is not null && !front)
            _pendingInFront = false;

        if (_pending is { IsCompleted: true } done)
        {
            _pending = null;
            _nextUtc = clock.UtcNow + Interval;
            var verdict = done.IsCompletedSuccessfully ? done.Result : FirewallVerdict.Unknown;

            if (_pendingGeneration == _generation && _watching && _pendingInFront)
            {
                switch (verdict)
                {
                    case FirewallVerdict.Blocked when ++_blockedReads >= 2:
                        Stop();
                        return true;

                    case FirewallVerdict.Blocked:
                        break;

                    case FirewallVerdict.NoRule:
                        _blockedReads = 0;
                        _noRuleFor += Interval;

                        if (_noRuleFor >= WatchFor)
                            Stop();

                        break;

                    default:
                        Stop();
                        break;
                }
            }
        }

        if (!_watching || _pending is not null)
            return false;

        // 手前にない間は読まない。戻ってきたら、そこから2回続けて確かめる。
        if (!front)
        {
            _blockedReads = 0;
            return false;
        }

        if (clock.UtcNow < _nextUtc)
            return false;

        _pendingGeneration = _generation;
        _pendingInFront = true;
        _pending = _run(check);
        return false;
    }

    /// <summary>ブロックされていたときに出す知らせ。直し方を添える。</summary>
    public static string BlockedNotice(string? program) =>
        "Windows のファイアウォールで、このアプリの受信がブロックされています。" +
        "そのため VRChat の AFK を検知できないので、AFK を使う設定をオフに戻しました。\n\n" +
        "使えるようにするには、次のどちらかを行ってから、もう一度オンにしてください。\n\n" +
        "・「Windows セキュリティ」→「ファイアウォールとネットワーク保護」→「ファイアウォールによるアプリケーションの許可」→「設定の変更」で、" +
        $"「{AppInfo.DisplayName}」の「プライベート」にチェックを入れる（同じ名前が複数あるときは「詳細」で下のパスのものを選ぶ）\n\n" +
        "・「セキュリティが強化された Windows Defender ファイアウォール」（wf.msc）の「受信の規則」で、" +
        $"「{AppInfo.DisplayName}」のうち操作が「ブロック」の規則を削除する（次にオンにしたときに、許可を求める画面がもう一度出ます）" +
        (program is null ? string.Empty : $"\n\n実行ファイル: {program}");
}
