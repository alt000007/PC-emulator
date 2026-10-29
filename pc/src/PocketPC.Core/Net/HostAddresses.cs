using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PocketPC.Core.Security;

namespace PocketPC.Core.Net;

/// <summary>The addresses a phone can use to reach this PC, best first: home LAN, then Tailscale.</summary>
public static class HostAddresses
{
    private static readonly string[] VirtualAdapterHints = ["vEthernet", "VirtualBox", "VMware", "WSL", "Hyper-V", "Loopback"];

    public static IReadOnlyList<string> Discover()
    {
        var lan = new List<string>();
        var tailnet = new List<string>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var isTailscale = nic.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)
                || nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase);
            if (!isTailscale && VirtualAdapterHints.Any(h =>
                    nic.Name.Contains(h, StringComparison.OrdinalIgnoreCase) ||
                    nic.Description.Contains(h, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip) || !NetworkPolicy.IsAllowed(ip))
                {
                    continue;
                }

                (IsTailnet(ip) ? tailnet : lan).Add(ip.ToString());
            }
        }

        return [.. lan.Distinct(), .. tailnet.Distinct()];
    }

    /// <summary>Tailscale hands out addresses from 100.64.0.0/10.</summary>
    public static bool IsTailnet(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return ip.AddressFamily == AddressFamily.InterNetwork && b[0] == 100 && (b[1] & 0xC0) == 64;
    }
}
