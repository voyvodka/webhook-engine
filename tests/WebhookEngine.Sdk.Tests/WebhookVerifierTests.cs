using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using WebhookEngine.Sdk;

namespace WebhookEngine.Sdk.Tests;

public class WebhookVerifierTests
{
    private const string WebhookId = "msg_2abc";
    private const string Body = """{"event":"order.created","id":42}""";

    // Mirrors the engine signer: signed content = "{id}.{ts}.{body}",
    // HMACSHA256 over UTF-8, base64, "v1," prefix. Secret bytes resolved the
    // same way WebhookVerifier.ResolveSecretBytes does so the test never drifts
    // from the production secret-encoding contract.
    private static string Sign(string webhookId, string timestamp, string body, string secret)
    {
        var signedContent = $"{webhookId}.{timestamp}.{body}";
        var secretBytes = ResolveSecretBytes(secret);
        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedContent));
        return $"v1,{Convert.ToBase64String(hash)}";
    }

    private static byte[] ResolveSecretBytes(string secret)
    {
        if (secret.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetBytes(secret);

        try
        {
            return Convert.FromBase64String(secret);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(secret);
        }
    }

    private static string NowTimestamp(TimeSpan offset = default) =>
        DateTimeOffset.UtcNow.Add(offset).ToUnixTimeSeconds().ToString();

    // A valid base64 secret (the engine generates base64 signing secrets).
    private static string Base64Secret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Verify_When_Signature_Valid_Within_Tolerance_Returns_True()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp();
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(WebhookId, ts, sig, Body, secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_When_Body_Tampered_Returns_False()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp();
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(WebhookId, ts, sig, Body + "tampered", secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_When_Secret_Wrong_Returns_False()
    {
        var ts = NowTimestamp();
        var sig = Sign(WebhookId, ts, Body, Base64Secret());

        var result = WebhookVerifier.Verify(WebhookId, ts, sig, Body, Base64Secret());

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("not-a-real-signature")]
    [InlineData("v2,abcdef")]
    [InlineData("AAAABBBBCCCC")]
    [InlineData("")]
    public void Verify_When_Signature_Has_No_V1_Prefix_Or_Garbage_Returns_False(string signature)
    {
        var secret = Base64Secret();
        var ts = NowTimestamp();

        var result = WebhookVerifier.Verify(WebhookId, ts, signature, Body, secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_When_Timestamp_Expired_Beyond_Default_Tolerance_Returns_False()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp(TimeSpan.FromMinutes(-10));
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(WebhookId, ts, sig, Body, secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_When_Future_Timestamp_Within_Tolerance_Returns_True()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp(TimeSpan.FromMinutes(2));
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(WebhookId, ts, sig, Body, secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_When_Future_Timestamp_Beyond_Tolerance_Returns_False()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp(TimeSpan.FromMinutes(10));
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(WebhookId, ts, sig, Body, secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_When_Custom_Tolerance_Allows_Older_Timestamp_Returns_True()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp(TimeSpan.FromMinutes(-10));
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(
            WebhookId, ts, sig, Body, secret, TimeSpan.FromMinutes(15));

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_When_Custom_Tolerance_Tighter_Than_Drift_Returns_False()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp(TimeSpan.FromMinutes(-2));
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(
            WebhookId, ts, sig, Body, secret, TimeSpan.FromMinutes(1));

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_When_Multiple_Space_Separated_Signatures_And_One_Matches_Returns_True()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp();
        var valid = Sign(WebhookId, ts, Body, secret);
        var combined = $"v1,bogusbogusbogus {valid} v1,anotherbogus";

        var result = WebhookVerifier.Verify(WebhookId, ts, combined, Body, secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_When_Multiple_Signatures_And_None_Match_Returns_False()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp();
        var combined = $"v1,bogusbogusbogus {Sign(WebhookId, ts, Body, Base64Secret())}";

        var result = WebhookVerifier.Verify(WebhookId, ts, combined, Body, secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_When_Secret_Has_Whsec_Prefix_Uses_Literal_Utf8_Bytes_Returns_True()
    {
        var secret = "whsec_MfKQ9r8t2vWxYz0aBcDeFgHiJkLmNoPq";
        var ts = NowTimestamp();
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(WebhookId, ts, sig, Body, secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_When_Secret_Is_Plain_Non_Base64_String_Falls_Back_To_Utf8_Returns_True()
    {
        // Contains chars not in the base64 alphabet → FromBase64String throws →
        // UTF-8 fallback path.
        var secret = "my-plain-text-secret!@#";
        var ts = NowTimestamp();
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(WebhookId, ts, sig, Body, secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_When_Secret_Is_Valid_Base64_Uses_Decoded_Bytes_Returns_True()
    {
        var secret = Base64Secret();
        var ts = NowTimestamp();
        var sig = Sign(WebhookId, ts, Body, secret);

        var result = WebhookVerifier.Verify(WebhookId, ts, sig, Body, secret);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "1700000000", "v1,abc", "secret")]
    [InlineData("msg_1", "", "v1,abc", "secret")]
    [InlineData("msg_1", "1700000000", "", "secret")]
    [InlineData("msg_1", "1700000000", "v1,abc", "")]
    [InlineData(null, "1700000000", "v1,abc", "secret")]
    [InlineData("msg_1", null, "v1,abc", "secret")]
    [InlineData("msg_1", "1700000000", null, "secret")]
    [InlineData("msg_1", "1700000000", "v1,abc", null)]
    public void Verify_When_Required_Field_Missing_Or_Empty_Returns_False(
        string? webhookId, string? timestamp, string? signature, string? secret)
    {
        var result = WebhookVerifier.Verify(webhookId!, timestamp!, signature!, Body, secret!);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("12.34")]
    [InlineData("ts_1700000000")]
    public void Verify_When_Timestamp_Not_Numeric_Returns_False(string timestamp)
    {
        var secret = Base64Secret();
        var sig = Sign(WebhookId, timestamp, Body, secret);

        var result = WebhookVerifier.Verify(WebhookId, timestamp, sig, Body, secret);

        result.Should().BeFalse();
    }
}
