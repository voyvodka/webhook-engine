using System.Net;
using FluentAssertions;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.Infrastructure.Tests.Services;

public class IpAllowlistMatcherTests
{
    [Fact]
    public void Parse_Returns_Empty_For_Null_Or_Whitespace()
    {
        IpAllowlistMatcher.Parse(null).Should().BeEmpty();
        IpAllowlistMatcher.Parse("").Should().BeEmpty();
        IpAllowlistMatcher.Parse("   ").Should().BeEmpty();
    }

    [Fact]
    public void Parse_Returns_Empty_For_Malformed_Json()
    {
        IpAllowlistMatcher.Parse("not-json").Should().BeEmpty();
        IpAllowlistMatcher.Parse("{}").Should().BeEmpty();
    }

    [Fact]
    public void Parse_Skips_Invalid_Cidr_Entries_But_Keeps_Valid_Ones()
    {
        var parsed = IpAllowlistMatcher.Parse("""["203.0.113.0/24","not-a-cidr","192.168.1.0/24"]""");
        parsed.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("203.0.113.0/24", true)]
    [InlineData("203.0.113.42/32", true)]
    [InlineData("2001:db8::/32", true)]
    [InlineData("not-a-cidr", false)]
    [InlineData("203.0.113.0/40", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseCidr_Recognises_Valid_Notations(string? cidr, bool expected)
    {
        IpAllowlistMatcher.TryParseCidr(cidr, out _).Should().Be(expected);
    }

    [Fact]
    public void AllAddressesAllowed_Returns_True_When_Every_Address_Matches()
    {
        var allowed = IpAllowlistMatcher.Parse("""["203.0.113.0/24","198.51.100.0/24"]""");
        var resolved = new[] { IPAddress.Parse("203.0.113.42"), IPAddress.Parse("198.51.100.7") };

        IpAllowlistMatcher.AllAddressesAllowed(allowed, resolved).Should().BeTrue();
    }

    [Fact]
    public void AllAddressesAllowed_Returns_False_When_One_Address_Sits_Outside_The_List()
    {
        var allowed = IpAllowlistMatcher.Parse("""["203.0.113.0/24"]""");
        var resolved = new[] { IPAddress.Parse("203.0.113.42"), IPAddress.Parse("8.8.8.8") };

        IpAllowlistMatcher.AllAddressesAllowed(allowed, resolved).Should().BeFalse();
    }

    [Fact]
    public void AllAddressesAllowed_Returns_True_When_Allowlist_Is_Empty()
    {
        var resolved = new[] { IPAddress.Parse("8.8.8.8") };
        IpAllowlistMatcher.AllAddressesAllowed([], resolved).Should().BeTrue();
    }

    [Fact]
    public void AllAddressesAllowed_Returns_True_When_Both_Allowlist_And_Resolution_Are_Empty()
    {
        // Empty allowlist short-circuits BEFORE the empty-resolution branch,
        // so the "allowlist not configured" semantics survive even when the
        // caller passes an empty resolved set. Today no production path lands
        // here (DeliveryWorker skips the matcher entirely when the allowlist
        // is empty), but the contract guard keeps a future caller honest.
        IpAllowlistMatcher.AllAddressesAllowed([], []).Should().BeTrue();
    }

    [Fact]
    public void AllAddressesAllowed_Returns_False_When_Resolution_Is_Empty_With_NonEmpty_List()
    {
        var allowed = IpAllowlistMatcher.Parse("""["203.0.113.0/24"]""");
        IpAllowlistMatcher.AllAddressesAllowed(allowed, []).Should().BeFalse();
    }
}
