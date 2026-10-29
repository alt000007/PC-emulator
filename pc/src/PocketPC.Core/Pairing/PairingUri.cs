using System.Text;

namespace PocketPC.Core.Pairing;

/// <summary>
/// What the QR code contains. Scanning it gives the phone everything it needs:
/// where the PC is, the certificate fingerprint to pin, and the one-time code.
///
///   pocketpc://pair?v=1&amp;id=&lt;pcId&gt;&amp;n=&lt;pc name&gt;&amp;h=&lt;host1,host2&gt;&amp;p=&lt;port&gt;&amp;fp=&lt;sha256&gt;&amp;c=&lt;code&gt;
/// </summary>
public sealed record PairingUri(
    string PcId,
    string PcName,
    IReadOnlyList<string> Hosts,
    int Port,
    string ServerFingerprint,
    string Code)
{
    public const string Scheme = "pocketpc";
    public const int Version = 1;

    public override string ToString()
    {
        var sb = new StringBuilder($"{Scheme}://pair?v={Version}");
        sb.Append("&id=").Append(Uri.EscapeDataString(PcId));
        sb.Append("&n=").Append(Uri.EscapeDataString(PcName));
        sb.Append("&h=").Append(string.Join(',', Hosts.Select(Uri.EscapeDataString)));
        sb.Append("&p=").Append(Port);
        sb.Append("&fp=").Append(ServerFingerprint);
        sb.Append("&c=").Append(Code);
        return sb.ToString();
    }

    public static PairingUri? TryParse(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Scheme ||
            uri.Host != "pair")
        {
            return null;
        }

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0])
            .ToDictionary(g => g.Key, g => g.First()[1]);

        if (!query.TryGetValue("v", out var v) || v != Version.ToString() ||
            !query.TryGetValue("id", out var id) ||
            !query.TryGetValue("n", out var name) ||
            !query.TryGetValue("h", out var hosts) ||
            !query.TryGetValue("p", out var portText) || !int.TryParse(portText, out var port) ||
            !query.TryGetValue("fp", out var fp) ||
            !query.TryGetValue("c", out var code) || PairingCode.Normalize(code) is not { } normalized)
        {
            return null;
        }

        return new PairingUri(
            Uri.UnescapeDataString(id),
            Uri.UnescapeDataString(name),
            hosts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList(),
            port,
            fp.ToLowerInvariant(),
            normalized);
    }
}
