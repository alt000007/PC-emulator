using System.Net;
using PocketPC.Core.Net;
using PocketPC.Core.Security;

namespace PocketPC.Core.Tests;

public class NetworkPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.50")]
    [InlineData("169.254.10.10")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd7a:115c:a1e0::1")]
    [InlineData("::ffff:192.168.1.50")]
    public void Allows_home_and_tailnet_addresses(string address) =>
        Assert.True(NetworkPolicy.IsAllowed(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("::ffff:8.8.8.8")]
    public void Blocks_public_internet_addresses(string address) =>
        Assert.False(NetworkPolicy.IsAllowed(IPAddress.Parse(address)));

    [Fact]
    public void Null_is_blocked() => Assert.False(NetworkPolicy.IsAllowed(null));

    [Fact]
    public void Comparable_string_unmaps_ipv4() =>
        Assert.Equal("192.168.1.50", NetworkPolicy.ToComparableString(IPAddress.Parse("::ffff:192.168.1.50")));

    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.100.100.100", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("192.168.1.1", false)]
    public void Detects_tailnet_addresses(string address, bool expected) =>
        Assert.Equal(expected, HostAddresses.IsTailnet(IPAddress.Parse(address)));
}
