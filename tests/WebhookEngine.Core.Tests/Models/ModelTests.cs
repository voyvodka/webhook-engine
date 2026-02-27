using FluentAssertions;
using WebhookEngine.Core.Models;

namespace WebhookEngine.Core.Tests.Models;

public class ModelTests
{
    [Fact]
    public void DeliveryRequest_Has_Expected_Defaults()
    {
        var request = new DeliveryRequest();

        request.MessageId.Should().BeEmpty();
        request.EndpointUrl.Should().BeEmpty();
        request.Payload.Should().Be("{}");
        request.SignedHeaders.Should().NotBeNull();
        request.CustomHeaders.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void DeliveryResult_Has_Expected_Defaults()
    {
        var result = new DeliveryResult();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(0);
        result.ResponseBody.Should().BeNull();
        result.Error.Should().BeNull();
        result.LatencyMs.Should().Be(0);
    }

    [Fact]
    public void SignedHeaders_Has_Expected_Defaults()
    {
        var headers = new SignedHeaders();

        headers.WebhookId.Should().BeEmpty();
        headers.WebhookTimestamp.Should().BeEmpty();
        headers.WebhookSignature.Should().BeEmpty();
    }
}
