using FluentAssertions;
using WebhookEngine.Core.Entities;

namespace WebhookEngine.Core.Tests.Entities;

public class EventTypeEntityTests
{
    [Fact]
    public void New_EventType_Has_Expected_Defaults()
    {
        var eventType = new EventType();

        eventType.Id.Should().Be(Guid.Empty);
        eventType.AppId.Should().Be(Guid.Empty);
        eventType.Name.Should().BeEmpty();
        eventType.Description.Should().BeNull();
        eventType.SchemaJson.Should().BeNull();
        eventType.IsArchived.Should().BeFalse();
        eventType.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Navigation_Collections_Are_Initialized_Empty()
    {
        var eventType = new EventType();
        eventType.Endpoints.Should().NotBeNull().And.BeEmpty();
    }
}
