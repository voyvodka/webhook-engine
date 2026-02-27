using FluentAssertions;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Tests.Entities;

public class MessageEntityTests
{
    [Fact]
    public void New_Message_Has_Expected_Defaults()
    {
        var message = new Message();

        message.Id.Should().Be(Guid.Empty);
        message.Payload.Should().Be("{}");
        message.Status.Should().Be(MessageStatus.Pending);
        message.AttemptCount.Should().Be(0);
        message.MaxRetries.Should().Be(7);
        message.ScheduledAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        message.LockedAt.Should().BeNull();
        message.LockedBy.Should().BeNull();
        message.DeliveredAt.Should().BeNull();
        message.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Attempts_Collection_Initialized_Empty()
    {
        var message = new Message();
        message.Attempts.Should().NotBeNull().And.BeEmpty();
    }
}
