using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.Repositories;

public class MessageRepositoryTests
{
    [Fact]
    public async Task CountAllAsync_Applies_App_Endpoint_And_Date_Filters()
    {
        await using var db = CreateDbContext();
        var repository = new MessageRepository(db);

        var app1 = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "App 1",
            ApiKeyPrefix = "whe_app1_",
            ApiKeyHash = "hash1",
            SigningSecret = "secret1"
        };

        var app2 = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "App 2",
            ApiKeyPrefix = "whe_app2_",
            ApiKeyHash = "hash2",
            SigningSecret = "secret2"
        };

        var endpoint1 = new Endpoint { Id = Guid.NewGuid(), AppId = app1.Id, Url = "https://example.com/ep1", Status = EndpointStatus.Active };
        var endpoint2 = new Endpoint { Id = Guid.NewGuid(), AppId = app1.Id, Url = "https://example.com/ep2", Status = EndpointStatus.Active };
        var endpoint3 = new Endpoint { Id = Guid.NewGuid(), AppId = app2.Id, Url = "https://example.com/ep3", Status = EndpointStatus.Active };

        var eventType1 = new EventType { Id = Guid.NewGuid(), AppId = app1.Id, Name = "order.created" };
        var eventType2 = new EventType { Id = Guid.NewGuid(), AppId = app2.Id, Name = "invoice.created" };

        var now = DateTime.UtcNow;

        var inScope = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app1.Id,
            EndpointId = endpoint1.Id,
            EventTypeId = eventType1.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = now.AddMinutes(-10),
            ScheduledAt = now.AddMinutes(-10)
        };

        var differentEndpoint = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app1.Id,
            EndpointId = endpoint2.Id,
            EventTypeId = eventType1.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = now.AddMinutes(-10),
            ScheduledAt = now.AddMinutes(-10)
        };

        var outOfRange = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app1.Id,
            EndpointId = endpoint1.Id,
            EventTypeId = eventType1.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = now.AddDays(-2),
            ScheduledAt = now.AddDays(-2)
        };

        var differentApp = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app2.Id,
            EndpointId = endpoint3.Id,
            EventTypeId = eventType2.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = now.AddMinutes(-10),
            ScheduledAt = now.AddMinutes(-10)
        };

        db.Applications.AddRange(app1, app2);
        db.Endpoints.AddRange(endpoint1, endpoint2, endpoint3);
        db.EventTypes.AddRange(eventType1, eventType2);
        db.Messages.AddRange(inScope, differentEndpoint, outOfRange, differentApp);
        await db.SaveChangesAsync();

        var count = await repository.CountAllAsync(
            app1.Id,
            MessageStatus.Delivered,
            endpoint1.Id,
            eventType: null,
            after: now.AddHours(-1),
            before: now,
            CancellationToken.None);

        count.Should().Be(1);
    }

    [Fact]
    public async Task ListReplayCandidatesAsync_Returns_Only_Active_Endpoint_And_Selected_Statuses()
    {
        await using var db = CreateDbContext();
        var repository = new MessageRepository(db);

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "App",
            ApiKeyPrefix = "whe_app_",
            ApiKeyHash = "hash",
            SigningSecret = "secret"
        };

        var eventType = new EventType
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Name = "order.created"
        };

        var activeEndpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/active",
            Status = EndpointStatus.Active
        };

        var disabledEndpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/disabled",
            Status = EndpointStatus.Disabled
        };

        var now = DateTime.UtcNow;

        var deliveredActive = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            EndpointId = activeEndpoint.Id,
            EventTypeId = eventType.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = now.AddMinutes(-20),
            ScheduledAt = now.AddMinutes(-20)
        };

        var failedActive = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            EndpointId = activeEndpoint.Id,
            EventTypeId = eventType.Id,
            Payload = "{}",
            Status = MessageStatus.Failed,
            CreatedAt = now.AddMinutes(-15),
            ScheduledAt = now.AddMinutes(-15)
        };

        var pendingActive = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            EndpointId = activeEndpoint.Id,
            EventTypeId = eventType.Id,
            Payload = "{}",
            Status = MessageStatus.Pending,
            CreatedAt = now.AddMinutes(-10),
            ScheduledAt = now.AddMinutes(-10)
        };

        var deliveredDisabled = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            EndpointId = disabledEndpoint.Id,
            EventTypeId = eventType.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = now.AddMinutes(-5),
            ScheduledAt = now.AddMinutes(-5)
        };

        db.Applications.Add(app);
        db.EventTypes.Add(eventType);
        db.Endpoints.AddRange(activeEndpoint, disabledEndpoint);
        db.Messages.AddRange(deliveredActive, failedActive, pendingActive, deliveredDisabled);
        await db.SaveChangesAsync();

        var result = await repository.ListReplayCandidatesAsync(
            app.Id,
            eventType.Id,
            endpointId: null,
            from: now.AddHours(-1),
            to: now,
            statuses: [MessageStatus.Delivered, MessageStatus.Failed],
            maxMessages: 10,
            CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(m => m.Id).Should().BeEquivalentTo([deliveredActive.Id, failedActive.Id]);
    }

    private static WebhookDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseInMemoryDatabase($"repo_tests_{Guid.NewGuid()}")
            .Options;

        return new WebhookDbContext(options);
    }
}
