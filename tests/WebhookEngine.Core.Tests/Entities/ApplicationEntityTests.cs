using FluentAssertions;
using WebhookEngine.Core.Entities;

namespace WebhookEngine.Core.Tests.Entities;

public class ApplicationEntityTests
{
    [Fact]
    public void New_Application_Has_Expected_Defaults()
    {
        var app = new Application();

        app.Id.Should().Be(Guid.Empty);
        app.Name.Should().BeEmpty();
        app.ApiKeyPrefix.Should().BeEmpty();
        app.ApiKeyHash.Should().BeEmpty();
        app.SigningSecret.Should().BeEmpty();
        app.IsActive.Should().BeTrue();
        app.RetryPolicyJson.Should().Contain("maxRetries");
        app.RetryPolicyJson.Should().Contain("backoffSchedule");
        app.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        app.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Navigation_Collections_Are_Initialized_Empty()
    {
        var app = new Application();

        app.EventTypes.Should().NotBeNull().And.BeEmpty();
        app.Endpoints.Should().NotBeNull().And.BeEmpty();
        app.Messages.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Default_RetryPolicyJson_Contains_Seven_Backoff_Values()
    {
        var app = new Application();

        // Default: [5,30,120,900,3600,21600,86400]
        app.RetryPolicyJson.Should().Contain("[5,30,120,900,3600,21600,86400]");
    }
}
