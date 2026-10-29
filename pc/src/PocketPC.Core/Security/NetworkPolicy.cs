using System.Net;
using System.Net.Sockets;

namespace PocketPC.Core.Security;

/// <summary>
/// Only devices on the home network or the private Tailscale/WireGuard network may talk to
/// the companion. Connections from public internet addresses are dropped before TLS even
/// starts, so a mistaken port-forward on the router still doesn't expose anything.
/// </summary>
public static class NetworkPolicy
{
    private static readonly (IPAddress Network, int PrefixLength)[] AllowedRanges =
    [
        (IPAddress.Parse("127.0.0.0"), 8),      // loopback
        (IPAddress.Parse("10.0.0.0"), 8),       // private LAN
        (IPAddress.Parse("172.16.0.0"), 12),    // private LAN
        (IPAddress.Parse("192.168.0.0"), 16),   // private LAN
        (IPAddress.Parse("169.254.0.0"), 16),   // link-local
        (IPAddress.Parse("100.64.0.0"), 10),    // CGNAT range used by Tailscale
        (IPAddress.IPv6Loopback, 128),
        (IPAddress.Parse("fe80::"), 10),        // IPv6 link-local
        (IPAddress.Parse("fc00::"), 7),         // IPv6 unique local (includes Tailscale fd7a:115c:a1e0::/48)
    ];

    public static bool IsAllowed(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        address = Normalize(address);
        foreach (var (network, prefix) in AllowedRanges)
        {
            if (IsInRange(address, network, prefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Maps IPv4-mapped IPv6 addresses (::ffff:a.b.c.d) back to plain IPv4.</summary>
    public static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>The same textual form Sunshine uses, so addresses from both sides can be compared.</summary>
    public static string ToComparableString(IPAddress address)
    {
        var normalized = Normalize(address);
        if (normalized.AddressFamily == AddressFamily.InterNetworkV6 && normalized.ScopeId != 0)
        {
            normalized = new IPAddress(normalized.GetAddressBytes());
        }

        return normalized.ToString();
    }

    private static bool IsInRange(IPAddress address, IPAddress network, int prefixLength)
    {
        if (address.AddressFamily != network.AddressFamily)
        {
            return false;
        }

        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        var fullBytes = prefixLength / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (a[i] != n[i])
            {
                return false;
            }
        }

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (a[fullBytes] & mask) == (n[fullBytes] & mask);
    }
}
