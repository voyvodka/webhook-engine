using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.Infrastructure.Tests.Services;

public class HmacSigningServiceTests
{
    private readonly HmacSigningService _sut = new();

    [Fact]
    public void Sign_Returns_Valid_SignedHeaders()
    {
        var messageId = Guid.NewGuid().ToString();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var body = """{"event":"order.created","data":{"id":123}}""";
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var result = _sut.Sign(messageId, timestamp, body, secret);

        result.WebhookId.Should().Be(messageId);
        result.WebhookTimestamp.Should().Be(timestamp.ToString());
        result.WebhookSignature.Should().StartWith("v1,");
    }

    [Fact]
    public void Sign_Produces_Correct_HMAC_SHA256()
    {
        var messageId = "msg_test123";
        var timestamp = 1700000000L;
        var body = """{"test":true}""";
        var secretBytes = new byte[32];
        Array.Fill(secretBytes, (byte)0xAB);
        var secret = Convert.ToBase64String(secretBytes);

        var result = _sut.Sign(messageId, timestamp, body, secret);

        // Manually compute expected signature
        var payload = $"{messageId}.{timestamp}.{body}";
        using var hmac = new HMACSHA256(secretBytes);
        var expectedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expectedSignature = $"v1,{Convert.ToBase64String(expectedHash)}";

        result.WebhookSignature.Should().Be(expectedSignature);
    }

    [Fact]
    public void Sign_Is_Deterministic()
    {
        var messageId = "msg_abc";
        var timestamp = 1700000000L;
        var body = """{"data":"hello"}""";
        var secret = Convert.ToBase64String(new byte[32]);

        var result1 = _sut.Sign(messageId, timestamp, body, secret);
        var result2 = _sut.Sign(messageId, timestamp, body, secret);

        result1.WebhookSignature.Should().Be(result2.WebhookSignature);
    }

    [Fact]
    public void Sign_Different_Secret_Produces_Different_Signature()
    {
        var messageId = "msg_abc";
        var timestamp = 1700000000L;
        var body = """{"data":"hello"}""";
        var secret1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secret2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var result1 = _sut.Sign(messageId, timestamp, body, secret1);
        var result2 = _sut.Sign(messageId, timestamp, body, secret2);

        result1.WebhookSignature.Should().NotBe(result2.WebhookSignature);
    }

    [Fact]
    public void Sign_Different_Body_Produces_Different_Signature()
    {
        var messageId = "msg_abc";
        var timestamp = 1700000000L;
        var secret = Convert.ToBase64String(new byte[32]);

        var result1 = _sut.Sign(messageId, timestamp, """{"a":1}""", secret);
        var result2 = _sut.Sign(messageId, timestamp, """{"a":2}""", secret);

        result1.WebhookSignature.Should().NotBe(result2.WebhookSignature);
    }

    [Fact]
    public void Sign_With_Whsec_Prefix_Uses_UTF8_Bytes()
    {
        var messageId = "msg_test";
        var timestamp = 1700000000L;
        var body = """{"test":true}""";
        var secret = "whsec_abc123def456";

        var result = _sut.Sign(messageId, timestamp, body, secret);

        // Verify it uses UTF8 bytes of the full whsec_ string
        var payload = $"{messageId}.{timestamp}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expectedSignature = $"v1,{Convert.ToBase64String(expectedHash)}";

        result.WebhookSignature.Should().Be(expectedSignature);
    }

    [Fact]
    public void Sign_With_NonBase64_Secret_Falls_Back_To_UTF8()
    {
        var messageId = "msg_test";
        var timestamp = 1700000000L;
        var body = """{"test":true}""";
        var secret = "this-is-not-base64!!!";

        var result = _sut.Sign(messageId, timestamp, body, secret);

        // Verify it uses UTF8 bytes
        var payload = $"{messageId}.{timestamp}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expectedSignature = $"v1,{Convert.ToBase64String(expectedHash)}";

        result.WebhookSignature.Should().Be(expectedSignature);
    }

    [Fact]
    public void Sign_With_Empty_Secret_Throws_InvalidOperationException()
    {
        var act = () => _sut.Sign("msg_1", 123, "{}", "");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*secret*missing*");
    }

    [Fact]
    public void Sign_With_Null_Secret_Throws_InvalidOperationException()
    {
        var act = () => _sut.Sign("msg_1", 123, "{}", null!);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*secret*missing*");
    }

    [Fact]
    public void Sign_With_Whitespace_Secret_Throws_InvalidOperationException()
    {
        var act = () => _sut.Sign("msg_1", 123, "{}", "   ");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*secret*missing*");
    }

    [Fact]
    public void Sign_With_Base64_Secret_Decodes_Correctly()
    {
        var rawBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var base64Secret = Convert.ToBase64String(rawBytes);

        var messageId = "msg_b64";
        var timestamp = 1700000000L;
        var body = """{"test":"base64"}""";

        var result = _sut.Sign(messageId, timestamp, body, base64Secret);

        // Verify it decoded the base64 and used the raw bytes
        var payload = $"{messageId}.{timestamp}.{body}";
        using var hmac = new HMACSHA256(rawBytes);
        var expectedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expectedSignature = $"v1,{Convert.ToBase64String(expectedHash)}";

        result.WebhookSignature.Should().Be(expectedSignature);
    }
}
