using System.Net;
using IniaAi.Common.Http;

namespace IniaAi.Common.Tests.Http;

public class OutboundAddressGuardTests
{
    [Theory]
    [InlineData("169.254.169.254", "link-local")]
    [InlineData("fe80::1", "link-local")]
    [InlineData("fd00:ec2::254", "unique-local")]
    [InlineData("fc00::1", "unique-local")]
    [InlineData("::1", "loopback")]
    [InlineData("127.0.0.1", "loopback")]
    [InlineData("127.255.255.254", "loopback")]
    [InlineData("0.0.0.0", "unspecified")]
    [InlineData("0.1.2.3", "unspecified")]
    [InlineData("::", "unspecified")]
    [InlineData("100.64.0.1", "carrier-grade NAT")]
    [InlineData("100.127.255.255", "carrier-grade NAT")]
    [InlineData("10.1.2.3", "private")]
    [InlineData("172.16.0.1", "private")]
    [InlineData("172.31.255.255", "private")]
    [InlineData("192.168.1.1", "private")]
    [InlineData("::ffff:169.254.169.254", "link-local")]
    [InlineData("::ffff:10.0.0.1", "private")]
    [InlineData("64:ff9b::a9fe:a9fe", "link-local")]
    [InlineData("192.0.0.170", "reserved")]
    [InlineData("240.0.0.1", "reserved")]
    [InlineData("255.255.255.255", "reserved")]
    [InlineData("198.18.0.1", "benchmarking")]
    [InlineData("198.19.255.255", "benchmarking")]
    [InlineData("::a00:1", "deprecated IPv4-compatible")]
    [InlineData("64:ff9b:1::a00:1", "local-use NAT64")]
    [InlineData("168.63.129.16", "cloud platform")]
    public void AnInternalAddress_BreaksItsRule(string address, string rule)
    {
        Assert.Equal(rule, InternalAddresses.RuleFor(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    [InlineData("192.0.1.1")]
    [InlineData("198.20.0.1")]
    [InlineData("223.255.255.1")]
    [InlineData("168.63.129.17")]
    public void APublicAddress_BreaksNoRule(string address)
    {
        Assert.Null(InternalAddresses.RuleFor(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("169.254.169.254", "link-local")]
    [InlineData("2852039166", "link-local")]
    [InlineData("[fd00:ec2::254]", "unique-local")]
    [InlineData("localhost", "loopback")]
    [InlineData("100.64.0.1", "carrier-grade NAT")]
    public async Task AnInternalHost_IsRefusedWithItsHostAndRule(string host, string rule)
    {
        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => OutboundAddressGuard.For([]).ResolveAsync(host, TestContext.Current.CancellationToken));

        Assert.Equal(rule, refusal.Rule);
        Assert.Contains(host, refusal.Message);
        Assert.Contains(rule, refusal.Message);
    }

    [Fact]
    public async Task ARefusedName_KeepsTheAddressItResolvesToOutOfTheMessage()
    {
        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => OutboundAddressGuard.For([]).ResolveAsync("localhost", TestContext.Current.CancellationToken));

        Assert.True(IPAddress.IsLoopback(refusal.Address));
        Assert.DoesNotContain(refusal.Address.ToString(), refusal.Message);
        Assert.Equal(
            "Refused to connect: localhost resolves to a loopback address. A workflow reaches an internal service only through an approved exception: see \"Calling an Internal Service from a Workflow\" in the platform documentation.",
            refusal.Message);
    }

    [Theory]
    [InlineData("0.0.0.0", "Refused to connect: 0.0.0.0 is an unspecified address.")]
    [InlineData("fd00::1", "Refused to connect: fd00::1 is a unique-local address.")]
    public async Task ARefusedAddress_IsNamedInAPlainSentence(string host, string sentence)
    {
        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => OutboundAddressGuard.For([]).ResolveAsync(host, TestContext.Current.CancellationToken));

        Assert.StartsWith(sentence, refusal.Message);
    }

    [Fact]
    public async Task APublicAddress_ResolvesToItself()
    {
        IReadOnlyList<IPAddress> addresses = await OutboundAddressGuard.For([])
            .ResolveAsync("8.8.8.8", TestContext.Current.CancellationToken);

        Assert.Equal([IPAddress.Parse("8.8.8.8")], addresses);
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("127.0.0.0/8", "127.0.0.1")]
    [InlineData("::ffff:127.0.0.1", "127.0.0.1")]
    [InlineData("LOCALHOST", "localhost")]
    [InlineData("localhost.", "localhost")]
    [InlineData("fd12::/16", "[fd12::1]")]
    public async Task AnAllowedInternalHost_Resolves(string entry, string host)
    {
        IReadOnlyList<IPAddress> addresses = await OutboundAddressGuard.For([entry])
            .ResolveAsync(host, TestContext.Current.CancellationToken);

        Assert.NotEmpty(addresses);
    }

    [Fact]
    public async Task AnAllowedAddress_AllowsNoOtherAddress()
    {
        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => OutboundAddressGuard.For(["127.0.0.1"]).ResolveAsync("127.0.0.2", TestContext.Current.CancellationToken));

        Assert.Equal("loopback", refusal.Rule);
    }

    [Fact]
    public void TheSameAllowList_GivesTheSameGuard()
    {
        Assert.Same(OutboundAddressGuard.For(["a.internal", "10.0.0.0/8"]), OutboundAddressGuard.For(["10.0.0.0/8", "A.internal"]));
    }

    [Theory]
    [InlineData("agent-runtime")]
    [InlineData("agent-runtime.internal")]
    [InlineData("10.0.0.0/8")]
    [InlineData("fd12::/16")]
    [InlineData("169.254.10.10")]
    public void AHostNameAnAddressOrARange_IsAnAllowListEntry(string entry)
    {
        _ = OutboundAddressGuard.For([entry]);
    }

    [Theory]
    [InlineData("10.0.0.0/33")]
    [InlineData("http://agent-runtime")]
    [InlineData("agent runtime")]
    [InlineData("agent-runtime:8130")]
    public void AnythingElse_IsRefusedAsAnAllowListEntry(string entry)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => OutboundAddressGuard.For([entry]));

        Assert.Contains(entry, error.Message);
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.0/16")]
    [InlineData("fd00:ec2::254")]
    [InlineData("fd00::/8")]
    [InlineData("168.63.129.16")]
    [InlineData("::ffff:169.254.169.254")]
    public void AnEntryThatCoversACloudMetadataService_StopsStartup(string entry)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => OutboundAddressGuard.For([entry]));

        Assert.Contains(entry, error.Message);
        Assert.Contains("Calling an Internal Service from a Workflow", error.Message);
    }

    [Fact]
    public async Task AnAllowedRange_HoldsTheIPv4AddressThatAnIPv6AddressCarries()
    {
        OutboundAddressGuard guard = OutboundAddressGuard.For(["10.0.0.0/8"]);

        Assert.NotEmpty(await guard.ResolveAsync("[64:ff9b::a00:5]", TestContext.Current.CancellationToken));
        Assert.NotEmpty(await guard.ResolveAsync("[::ffff:10.0.0.5]", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ARefusalWrappedByTheHttpStack_IsFound()
    {
        var refusal = new OutboundAddressRefusedException("metadata", IPAddress.Parse("169.254.169.254"), "link-local");
        var wrapped = new HttpRequestException("send failed", new IOException("connect failed", refusal));

        Assert.Same(refusal, OutboundAddressRefusedException.FindIn(wrapped));
        Assert.Null(OutboundAddressRefusedException.FindIn(new HttpRequestException("send failed")));
    }
}
