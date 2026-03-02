using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.Repositories;

public class EndpointRepositoryTests
{
    [Fact]
    public async Task CountByAppIdAsync_Respects_Status_Filter()
    {
        await using var db = CreateDbContext();
        var repository = new EndpointRepository(db);

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "App",
            ApiKeyPrefix = "whe_app_",
            ApiKeyHash = "hash",
            SigningSecret = "secret"
        };

        var active1 = new Endpoint { Id = Guid.NewGuid(), AppId = app.Id, Url = "https://example.com/1", Status = EndpointStatus.Active };
        var active2 = new Endpoint { Id = Guid.NewGuid(), AppId = app.Id, Url = "https://example.com/2", Status = EndpointStatus.Active };
        var disabled = new Endpoint { Id = Guid.NewGuid(), AppId = app.Id, Url = "https://example.com/3", Status = EndpointStatus.Disabled };

        db.Applications.Add(app);
        db.Endpoints.AddRange(active1, active2, disabled);
        await db.SaveChangesAsync();

        var activeCount = await repository.CountByAppIdAsync(app.Id, EndpointStatus.Active, CancellationToken.None);
        var disabledCount = await repository.CountByAppIdAsync(app.Id, EndpointStatus.Disabled, CancellationToken.None);
        var allCount = await repository.CountByAppIdAsync(app.Id, null, CancellationToken.None);

        activeCount.Should().Be(2);
        disabledCount.Should().Be(1);
        allCount.Should().Be(3);
    }

    [Fact]
    public async Task GetSubscribedEndpointsAsync_Returns_Active_Endpoints_With_Matching_Or_Empty_Filters()
    {
        await using var db = CreateDbContext();
        var repository = new EndpointRepository(db);

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "App",
            ApiKeyPrefix = "whe_app_",
            ApiKeyHash = "hash",
            SigningSecret = "secret"
        };

        var eventTypeA = new EventType { Id = Guid.NewGuid(), AppId = app.Id, Name = "order.created" };
        var eventTypeB = new EventType { Id = Guid.NewGuid(), AppId = app.Id, Name = "order.cancelled" };

        var unfilteredEndpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/unfiltered",
            Status = EndpointStatus.Active
        };

        var matchingEndpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/matching",
            Status = EndpointStatus.Active,
            EventTypes = [eventTypeA]
        };

        var nonMatchingEndpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/non-matching",
            Status = EndpointStatus.Active,
            EventTypes = [eventTypeB]
        };

        var disabledEndpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/disabled",
            Status = EndpointStatus.Disabled,
            EventTypes = [eventTypeA]
        };

        db.Applications.Add(app);
        db.EventTypes.AddRange(eventTypeA, eventTypeB);
        db.Endpoints.AddRange(unfilteredEndpoint, matchingEndpoint, nonMatchingEndpoint, disabledEndpoint);
        await db.SaveChangesAsync();

        var subscribed = await repository.GetSubscribedEndpointsAsync(app.Id, eventTypeA.Id, CancellationToken.None);

        subscribed.Should().HaveCount(2);
        subscribed.Select(e => e.Id).Should().BeEquivalentTo([unfilteredEndpoint.Id, matchingEndpoint.Id]);
    }

    private static WebhookDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseInMemoryDatabase($"endpoint_repo_tests_{Guid.NewGuid()}")
            .Options;

        return new WebhookDbContext(options);
    }
}
