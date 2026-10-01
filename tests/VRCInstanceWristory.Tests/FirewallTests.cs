using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// AFK の受け口がファイアウォールでブロックされていないかの判定と見張り（2026-10-01のユーザー指定→実装メモ5.111）。
/// 本物の規則は読まない（利用者の環境で結果が変わるため）。読み出した後の判定と、見張りの動きを確かめる。
/// </summary>
public class FirewallTests
{
    private const string Program = @"C:\Tools\VRCInstanceWristory\VRCInstanceWristory.exe";
    private const int Private = 2;
    private const int Public = 4;

    private static FirewallRule Rule(bool block, int protocol = WindowsFirewall.Udp, string? ports = "*", int profiles = Private | Public,
        string? program = Program, bool enabled = true, bool inbound = true)
        => new(program, enabled, inbound, block, protocol, ports, profiles);

    private static FirewallPolicy Policy(params FirewallRule[] rules)
        => new(Private, _ => true, _ => false, rules);

    // ---------------------------------------------------------------- 判定

    [Fact]
    public void 規則がなければ_許可を求める画面が出る扱い()
    {
        Assert.Equal(FirewallVerdict.NoRule, WindowsFirewall.Evaluate(Policy(), Program));
        Assert.Equal(FirewallVerdict.NoRule, WindowsFirewall.Evaluate(Policy(Rule(block: true, program: @"C:\other\app.exe")), Program));
    }

    [Fact]
    public void 許可しないで作られたブロックの規則があれば_ブロック()
    {
        // 「許可しない」で Windows が作る規則は TCP と UDP の2つ（ポートはすべて）。
        var policy = Policy(Rule(block: true, protocol: WindowsFirewall.Tcp), Rule(block: true));
        Assert.Equal(FirewallVerdict.Blocked, WindowsFirewall.Evaluate(policy, Program));
    }

    [Fact]
    public void 許可とブロックの両方があれば_ブロックが勝つ()
    {
        Assert.Equal(FirewallVerdict.Blocked, WindowsFirewall.Evaluate(Policy(Rule(block: false), Rule(block: true)), Program));
        Assert.Equal(FirewallVerdict.Allowed, WindowsFirewall.Evaluate(Policy(Rule(block: false)), Program));
    }

    [Fact]
    public void 実行ファイルのパスは大文字小文字と環境変数を区別せずに比べる()
    {
        Assert.Equal(FirewallVerdict.Blocked, WindowsFirewall.Evaluate(Policy(Rule(block: true, program: Program.ToLowerInvariant())), Program));

        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var notepad = Path.Combine(windir, "notepad.exe");
        Assert.Equal(FirewallVerdict.Blocked, WindowsFirewall.Evaluate(Policy(Rule(block: true, program: @"%SystemRoot%\notepad.exe")), notepad));
    }

    [Fact]
    public void mDNSに当てはまらない規則は使わない()
    {
        // TCP だけ・5353 を含まないポート・無効・送信・いまのネットワークでない。
        var policy = Policy(
            Rule(block: true, protocol: WindowsFirewall.Tcp),
            Rule(block: true, ports: "80,443"),
            Rule(block: true, enabled: false),
            Rule(block: true, inbound: false),
            Rule(block: true, profiles: Public));

        Assert.Equal(FirewallVerdict.NoRule, WindowsFirewall.Evaluate(policy, Program));

        // プロトコルがすべて・ポートの範囲に入る。
        Assert.Equal(FirewallVerdict.Blocked, WindowsFirewall.Evaluate(Policy(Rule(block: true, protocol: WindowsFirewall.AnyProtocol, ports: null)), Program));
        Assert.Equal(FirewallVerdict.Blocked, WindowsFirewall.Evaluate(Policy(Rule(block: true, ports: "80, 5000-6000")), Program));
    }

    [Fact]
    public void ファイアウォールが無効なら許可_受信をすべて止める設定ならブロック()
    {
        var rules = new[] { Rule(block: true) };
        Assert.Equal(FirewallVerdict.Allowed, WindowsFirewall.Evaluate(new FirewallPolicy(Private, _ => false, _ => false, rules), Program));
        Assert.Equal(FirewallVerdict.Blocked, WindowsFirewall.Evaluate(new FirewallPolicy(Private, _ => true, _ => true, []), Program));

        // つながっているネットワークが2つなら、どちらかでブロックされていればブロック。
        var oneSide = new FirewallPolicy(Private | Public, _ => true, _ => false, [Rule(block: false, profiles: Private), Rule(block: true, profiles: Public)]);
        Assert.Equal(FirewallVerdict.Blocked, WindowsFirewall.Evaluate(oneSide, Program));
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("5353", true)]
    [InlineData("53", false)]
    [InlineData("5000-6000", true)]
    [InlineData("6000-7000", false)]
    [InlineData("RPC", false)]
    [InlineData("", true)]
    public void ポートの指定(string ports, bool includes)
        => Assert.Equal(includes, WindowsFirewall.PortsInclude(ports, 5353));

    // ---------------------------------------------------------------- 見張り

    private sealed class Rules
    {
        public FirewallVerdict Verdict { get; set; } = FirewallVerdict.NoRule;

        public int Reads { get; private set; }

        public FirewallVerdict Read()
        {
            Reads++;
            return Verdict;
        }
    }

    /// <summary>見張りと、その相手（規則・このアプリが手前か・時計）。</summary>
    private sealed class Harness
    {
        public Rules Rules { get; } = new();

        public bool InFront { get; set; } = true;

        public ManualClock Clock { get; } = new(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        public AfkFirewallWatch Watch { get; }

        public Harness()
        {
            // 読むのはその場で済ませる（本物は別のスレッド）。結果を受け取るのは次の Poll。
            Watch = new AfkFirewallWatch(Rules.Read, () => InFront, Clock, check => Task.FromResult(check()));
        }

        /// <summary>間隔ぶん進めて、読み始めと受け取りの2回 Poll する。知らせたら true。</summary>
        public bool Step(FirewallVerdict verdict, bool inFront = true)
        {
            Rules.Verdict = verdict;
            InFront = inFront;
            var told = Watch.Poll() | Watch.Poll();
            Clock.Advance(AfkFirewallWatch.Interval);
            return told;
        }
    }

    [Fact]
    public void すでにブロックされていれば_手前で2回続けて読んでから1回だけ知らせる()
    {
        var h = new Harness();

        Assert.False(h.Watch.Poll());
        h.Watch.Start();

        Assert.False(h.Step(FirewallVerdict.Blocked));
        Assert.True(h.Watch.Watching);
        Assert.True(h.Step(FirewallVerdict.Blocked));
        Assert.False(h.Watch.Watching);
        Assert.False(h.Step(FirewallVerdict.Blocked));
        Assert.Equal(2, h.Rules.Reads);
    }

    [Fact]
    public void 許可を求める画面が出ている間は_どれだけ長くても読まず知らせない()
    {
        // 画面を出すと同時に Windows はブロックの規則を作る（→実装メモ5.112）。画面が手前にある間は、このアプリは手前でない。
        var h = new Harness();
        h.Watch.Start();

        for (var i = 0; i < 300; i++)
            Assert.False(h.Step(FirewallVerdict.Blocked, inFront: false));

        Assert.True(h.Watch.Watching);
        Assert.Equal(0, h.Rules.Reads);

        // 「許可」を押すと、このアプリへ戻る。書き換えの途中の一瞬はまだブロックで読めることがある。
        Assert.False(h.Step(FirewallVerdict.Blocked));
        Assert.False(h.Step(FirewallVerdict.Allowed));
        Assert.False(h.Watch.Watching);
    }

    [Fact]
    public void 許可しないを選んでこのアプリへ戻ると_2回続けてブロックで知らせる()
    {
        var h = new Harness();
        h.Watch.Start();

        Assert.False(h.Step(FirewallVerdict.NoRule));
        Assert.False(h.Step(FirewallVerdict.Blocked, inFront: false));
        Assert.False(h.Step(FirewallVerdict.Blocked, inFront: false));

        Assert.False(h.Step(FirewallVerdict.Blocked));
        Assert.True(h.Step(FirewallVerdict.Blocked));
    }

    [Fact]
    public void 手前を離れると数え直す_読んでいる途中で離れた結果も使わない()
    {
        var h = new Harness();
        h.Watch.Start();

        Assert.False(h.Step(FirewallVerdict.Blocked));
        Assert.False(h.Step(FirewallVerdict.Blocked, inFront: false));
        Assert.False(h.Step(FirewallVerdict.Blocked));

        // 読み始めたあとに許可を求める画面が出た（手前でなくなった）。
        h.Rules.Verdict = FirewallVerdict.Blocked;
        h.InFront = true;
        Assert.False(h.Watch.Poll());
        h.InFront = false;
        Assert.False(h.Watch.Poll());
        h.Clock.Advance(AfkFirewallWatch.Interval);

        Assert.True(h.Watch.Watching);
        Assert.False(h.Step(FirewallVerdict.Blocked));
        Assert.True(h.Step(FirewallVerdict.Blocked));
    }

    [Fact]
    public void 許可されれば見張りを終える_読めなければ開いたままにする()
    {
        foreach (var verdict in new[] { FirewallVerdict.Allowed, FirewallVerdict.Unknown })
        {
            var h = new Harness();
            h.Watch.Start();

            Assert.False(h.Step(verdict));
            Assert.False(h.Watch.Watching);
        }
    }

    [Fact]
    public void 規則がないまま手前で読み続けたら見張りを終える()
    {
        var h = new Harness();
        h.Watch.Start();

        var steps = (int)(AfkFirewallWatch.WatchFor / AfkFirewallWatch.Interval);

        for (var i = 0; i < steps - 1; i++)
            Assert.False(h.Step(FirewallVerdict.NoRule));

        Assert.True(h.Watch.Watching);
        Assert.False(h.Step(FirewallVerdict.NoRule));
        Assert.False(h.Watch.Watching);
    }

    [Fact]
    public void 閉じたあとに届いた結果は使わない()
    {
        var h = new Harness();
        h.Rules.Verdict = FirewallVerdict.Blocked;

        h.Watch.Start();
        Assert.False(h.Step(FirewallVerdict.Blocked));

        h.Watch.Poll();   // 読み始める
        h.Watch.Stop();   // その間に利用者がオフにした
        Assert.False(h.Watch.Poll());
        h.Clock.Advance(AfkFirewallWatch.Interval);

        // もう一度オンにすると、改めて2回読んで知らせる（前のブロックの読みは数えない）。
        h.Watch.Start();
        Assert.False(h.Step(FirewallVerdict.Blocked));
        Assert.True(h.Step(FirewallVerdict.Blocked));
    }

    [Fact]
    public void 知らせには直し方と実行ファイルのパスを入れる()
    {
        var text = AfkFirewallWatch.BlockedNotice(Program);

        Assert.Contains("ブロック", text);
        Assert.Contains("オフに戻しました", text);
        Assert.Contains("ファイアウォールによるアプリケーションの許可", text);
        Assert.Contains("wf.msc", text);
        Assert.Contains(Program, text);
    }
}
