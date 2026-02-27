using FluentAssertions;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Tests.Entities;

public class MessageAttemptEntityTests
{
    [Fact]
    public void New_MessageAttempt_Has_Expected_Defaults()
    {
        var attempt = new MessageAttempt();

        attempt.Id.Should().Be(Guid.Empty);
        attempt.MessageId.Should().Be(Guid.Empty);
        attempt.EndpointId.Should().Be(Guid.Empty);
        attempt.AttemptNumber.Should().Be(0);
        attempt.Status.Should().Be(AttemptStatus.Success); // first enum value
        attempt.StatusCode.Should().BeNull();
        attempt.RequestHeadersJson.Should().BeNull();
        attempt.ResponseBody.Should().BeNull();
        attempt.Error.Should().BeNull();
        attempt.LatencyMs.Should().Be(0);
        attempt.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
