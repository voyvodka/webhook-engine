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

    [Fact]
    public void New_Application_Defaults_Both_Portal_Fields_To_Null()
    {
        var app = new Application();

        // Portal access is opt-in; a freshly-created application has no
        // signing key and no allowed origins, which is the "portal disabled"
        // state the API auth path will check for.
        app.PortalSigningKey.Should().BeNull();
        app.AllowedPortalOriginsJson.Should().BeNull();
    }

    [Fact]
    public void Application_Can_Set_And_Read_PortalSigningKey_And_AllowedPortalOriginsJson()
    {
        var app = new Application
        {
            PortalSigningKey = "base64-encoded-32-byte-secret",
            AllowedPortalOriginsJson = """["https://app.acme.com","https://staging.acme.com"]"""
        };

        app.PortalSigningKey.Should().Be("base64-encoded-32-byte-secret");
        app.AllowedPortalOriginsJson.Should().Be("""["https://app.acme.com","https://staging.acme.com"]""");
    }
}
