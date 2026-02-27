using FluentAssertions;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Tests.Entities;

public class EndpointEntityTests
{
    [Fact]
    public void New_Endpoint_Has_Expected_Defaults()
    {
        var endpoint = new Endpoint();

        endpoint.Id.Should().Be(Guid.Empty);
        endpoint.AppId.Should().Be(Guid.Empty);
        endpoint.Url.Should().BeEmpty();
        endpoint.Description.Should().BeNull();
        endpoint.Status.Should().Be(EndpointStatus.Active);
        endpoint.CustomHeadersJson.Should().Be("{}");
        endpoint.SecretOverride.Should().BeNull();
        endpoint.MetadataJson.Should().Be("{}");
        endpoint.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        endpoint.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Navigation_Collections_Are_Initialized_Empty()
    {
        var endpoint = new Endpoint();

        endpoint.EventTypes.Should().NotBeNull().And.BeEmpty();
        endpoint.Messages.Should().NotBeNull().And.BeEmpty();
        endpoint.Health.Should().BeNull();
    }
}
