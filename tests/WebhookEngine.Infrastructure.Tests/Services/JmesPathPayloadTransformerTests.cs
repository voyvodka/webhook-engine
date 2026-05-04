using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.Infrastructure.Tests.Services;

public class JmesPathPayloadTransformerTests
{
    // Timeout default is intentionally generous in tests because CI runners
    // (especially the first warmup of JmesPath.Net's JIT) can take >100ms
    // for a single evaluation. Production default is 100ms — see
    // TransformationOptions. Tests that specifically exercise the timeout path
    // pass a low value explicitly.
    private const int CiSafeTimeoutMs = 5000;

    private static JmesPathPayloadTransformer CreateTransformer(
        int timeoutMs = CiSafeTimeoutMs,
        int maxOutputBytes = 256 * 1024)
    {
        var options = Options.Create(new TransformationOptions
        {
            Enabled = true,
            TimeoutMs = timeoutMs,
            MaxOutputBytes = maxOutputBytes
        });
        return new JmesPathPayloadTransformer(options, NullLogger<JmesPathPayloadTransformer>.Instance);
    }

    [Fact]
    public void Transform_Reshape_Returns_Projected_Payload()
    {
        var transformer = CreateTransformer();
        var payload = """{"user":{"name":"Alice","email":"alice@example.com","ssn":"123-45-6789"},"event":"order.created"}""";
        var expression = "{userName: user.name, userEmail: user.email, eventType: event}";

        var result = transformer.Transform(expression, payload);

        result.IsSuccess.Should().BeTrue();
        result.TransformedPayload.Should().Contain("\"userName\"");
        result.TransformedPayload.Should().Contain("Alice");
        result.TransformedPayload.Should().NotContain("ssn");
        result.TransformedPayload.Should().NotContain("123-45-6789");
    }

    [Fact]
    public void Transform_Identity_Selector_Returns_Same_Shape()
    {
        var transformer = CreateTransformer();
        var payload = """{"a":1,"b":2}""";

        var result = transformer.Transform("@", payload);

        result.IsSuccess.Should().BeTrue();
        result.TransformedPayload.Should().Contain("\"a\"");
        result.TransformedPayload.Should().Contain("\"b\"");
    }

    [Fact]
    public void Transform_Invalid_Expression_Fails_Open_With_Error()
    {
        var transformer = CreateTransformer();
        var payload = """{"foo":"bar"}""";

        var result = transformer.Transform("not a valid jmespath @@!", payload);

        result.IsSuccess.Should().BeFalse();
        result.TransformedPayload.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Transform_Empty_Expression_Fails_Open()
    {
        var transformer = CreateTransformer();

        var result = transformer.Transform("", """{"foo":"bar"}""");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("empty", because: "empty expression should not silently pass through");
    }

    [Fact]
    public void Transform_Output_Larger_Than_Limit_Fails_Open()
    {
        var transformer = CreateTransformer(maxOutputBytes: 32);
        // Build a payload that, projected as-is via "@", exceeds 32 bytes.
        var payload = """{"items":["a","b","c","d","e","f","g","h"]}""";

        var result = transformer.Transform("@", payload);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("exceeds", because: "the size guard should report a clear over-limit error");
    }

    [Fact]
    public void Transform_Invalid_Json_Payload_Fails_Open()
    {
        var transformer = CreateTransformer();

        var result = transformer.Transform("user.name", "not-json{");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }
}
