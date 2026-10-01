using System.Globalization;
using System.Runtime.InteropServices;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>この実行ファイルが受け取る通信を、Windows のファイアウォールがどう扱うか（→実装メモ5.111）。</summary>
public enum FirewallVerdict
{
    /// <summary>規則を読めなかった（ほかのファイアウォールに任せている・読む権限がないなど）。これまでどおり受け口を開く。</summary>
    Unknown,

    /// <summary>この実行ファイルの規則がまだない。受け口を開くと、Windows が許可を求める画面を出す。</summary>
    NoRule,

    /// <summary>許可されている（またはいまのネットワークでファイアウォールが無効）。</summary>
    Allowed,

    /// <summary>ブロックの規則がある。受け口は開けるが、外からの通信は届かず、許可を求める画面ももう出ない。</summary>
    Blocked,
}

/// <summary>受信の規則のうち、判定に使う分だけ。</summary>
/// <param name="Protocol">IANA の番号。<see cref="WindowsFirewall.AnyProtocol"/> はすべて。</param>
/// <param name="Profiles">当てはまるプロファイル（ドメイン=1・プライベート=2・パブリック=4 の組み合わせ）。</param>
public sealed record FirewallRule(string? Program, bool Enabled, bool Inbound, bool Block, int Protocol, string? LocalPorts, int Profiles);

/// <summary>ファイアウォールの全体の設定のうち、判定に使う分だけ。</summary>
/// <param name="CurrentProfiles">いまつながっているネットワークのプロファイル（組み合わせ）。</param>
/// <param name="Enabled">そのプロファイルでファイアウォールが有効か。</param>
/// <param name="BlockAllInbound">そのプロファイルで、許可の規則があっても受信をすべて止めるか。</param>
public sealed record FirewallPolicy(int CurrentProfiles, Func<int, bool> Enabled, Func<int, bool> BlockAllInbound, IReadOnlyList<FirewallRule> Rules);

/// <summary>
/// Windows のファイアウォールの規則を読み、AFK の受け口（mDNS・UDP 5353→実装メモ5.89）がブロックされていないかを判定する（→実装メモ5.111）。
///
/// 許可を求める画面で「許可しない」（キャンセル）を選ぶと、Windows はその実行ファイルの受信をブロックする規則を作る。
/// 規則ができたあとは、受け口を開いても画面はもう出ず、受け口を開くこと自体も成功するので、アプリからは塞がれていることが分からない。
/// そのため、受け口を開く前とあとに規則を読んで確かめる。読むだけで、規則は変えない。
/// 読むのは公開されている COM の <c>HNetCfg.FwPolicy2</c>（INetFwPolicy2）。
///
/// 許可を求める画面が出ている間も、規則はブロックになっている（Windows が画面を出すと同時にブロックで作り、答えで書き換える→実装メモ5.112）。
/// この間の規則は「許可しない」を選んだあとと中身が同じなので、規則だけでは見分けられない。いつ決めるかは <see cref="Modes.AfkFirewallWatch"/> が受け持つ。
/// </summary>
public static class WindowsFirewall
{
    public const int AnyProtocol = 256;
    public const int Udp = 17;
    public const int Tcp = 6;

    /// <summary>AFK の受け口のうち、LAN 側で待つもの（mDNS）。</summary>
    public const int MdnsPort = 5353;

    private static readonly int[] ProfileBits = [1, 2, 4];

    /// <summary>この実行ファイルの mDNS（UDP 5353）の受信を、いまのネットワークでどう扱うか。</summary>
    public static FirewallVerdict Evaluate(FirewallPolicy policy, string program)
    {
        var anyRule = false;
        var anyEnabledProfile = false;

        foreach (var profile in ProfileBits)
        {
            if ((policy.CurrentProfiles & profile) == 0 || !policy.Enabled(profile))
                continue;

            anyEnabledProfile = true;

            if (policy.BlockAllInbound(profile))
                return FirewallVerdict.Blocked;

            foreach (var rule in policy.Rules)
            {
                if (!Applies(rule, program, profile))
                    continue;

                // 許可とブロックの両方があれば、ブロックが勝つ（Windows のファイアウォールの決まり）。
                if (rule.Block)
                    return FirewallVerdict.Blocked;

                anyRule = true;
            }
        }

        if (!anyEnabledProfile)
            return FirewallVerdict.Allowed;

        return anyRule ? FirewallVerdict.Allowed : FirewallVerdict.NoRule;
    }

    private static bool Applies(FirewallRule rule, string program, int profile)
        => rule.Enabled
           && rule.Inbound
           && (rule.Profiles & profile) != 0
           && SameProgram(rule.Program, program)
           && (rule.Protocol == AnyProtocol || (rule.Protocol == Udp && PortsInclude(rule.LocalPorts, MdnsPort)));

    private static bool SameProgram(string? ruleProgram, string program)
    {
        if (string.IsNullOrWhiteSpace(ruleProgram))
            return false;

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(ruleProgram.Trim());
            return string.Equals(Path.GetFullPath(expanded), Path.GetFullPath(program), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>規則のポートの指定（<c>*</c>・<c>5353</c>・<c>5000-6000</c>・それらをカンマで並べたもの）に、そのポートが入るか。</summary>
    public static bool PortsInclude(string? ports, int port)
    {
        if (string.IsNullOrWhiteSpace(ports))
            return true;

        foreach (var raw in ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw == "*")
                return true;

            var dash = raw.IndexOf('-');

            if (dash > 0
                && int.TryParse(raw[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
                && int.TryParse(raw[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var to))
            {
                if (from <= port && port <= to)
                    return true;
            }
            else if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var single) && single == port)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>この実行ファイルについて、いまの規則を読んで判定する。読めなければ <see cref="FirewallVerdict.Unknown"/>。</summary>
    public static FirewallVerdict Check(string? program, IDiagnostics log)
    {
        if (string.IsNullOrEmpty(program))
            return FirewallVerdict.Unknown;

        try
        {
            return Read() is { } policy ? Evaluate(policy, program) : FirewallVerdict.Unknown;
        }
        catch (Exception ex)
        {
            log.Info($"Windows のファイアウォールの規則を読めません: {ex.Message}");
            return FirewallVerdict.Unknown;
        }
    }

    /// <summary>INetFwPolicy2 から、判定に使う分を読み出す。</summary>
    private static FirewallPolicy? Read()
    {
        if (Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: false) is not { } type)
            return null;

        dynamic policy = Activator.CreateInstance(type)!;

        try
        {
            int current = policy.CurrentProfileTypes;
            var enabled = new Dictionary<int, bool>();
            var blockAll = new Dictionary<int, bool>();

            foreach (var profile in ProfileBits)
            {
                enabled[profile] = (bool)policy.FirewallEnabled[profile];
                blockAll[profile] = (bool)policy.BlockAllInboundTraffic[profile];
            }

            var rules = new List<FirewallRule>();
            dynamic all = policy.Rules;

            foreach (dynamic rule in all)
            {
                try
                {
                    // 実行ファイルを決めていない規則は判定に使わないので、先に除いて読む量を減らす。
                    string? program = rule.ApplicationName;

                    if (string.IsNullOrEmpty(program))
                        continue;

                    rules.Add(new FirewallRule(
                        program,
                        (bool)rule.Enabled,
                        (int)rule.Direction == 1,
                        (int)rule.Action == 0,
                        (int)rule.Protocol,
                        (string?)rule.LocalPorts,
                        (int)rule.Profiles));
                }
                finally
                {
                    Marshal.ReleaseComObject(rule);
                }
            }

            Marshal.ReleaseComObject(all);
            return new FirewallPolicy(current, p => enabled[p], p => blockAll[p], rules);
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }
}
