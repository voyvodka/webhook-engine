using FluentAssertions;
using WebhookEngine.Core.Entities;

namespace WebhookEngine.Core.Tests.Entities;

public class DashboardUserEntityTests
{
    [Fact]
    public void New_DashboardUser_Has_Expected_Defaults()
    {
        var user = new DashboardUser();

        user.Id.Should().Be(Guid.Empty);
        user.Email.Should().BeEmpty();
        user.PasswordHash.Should().BeEmpty();
        user.Role.Should().Be("admin");
        user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        user.LastLoginAt.Should().BeNull();
    }
}
