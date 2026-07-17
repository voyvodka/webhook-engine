using FluentAssertions;
using WebhookEngine.API.Contracts;

namespace WebhookEngine.API.Tests.Contracts;

// A11: the real guard against Npgsql rejecting an Unspecified DateTime bound to a
// timestamptz parameter. Kind is normalized to Utc; the instant is preserved.
public class TimestampBoundsTests
{
    [Fact]
    public void AsUtc_Null_Returns_Null()
    {
        TimestampBounds.AsUtc((DateTime?)null).Should().BeNull();
    }

    [Fact]
    public void AsUtc_Unspecified_Is_Relabeled_Utc_Without_Shifting_Ticks()
    {
        var unspecified = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = TimestampBounds.AsUtc(unspecified);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Ticks.Should().Be(unspecified.Ticks, "an unspecified kind is treated as already-UTC, not shifted");
    }

    [Fact]
    public void AsUtc_Local_Is_Converted_To_Universal_Time()
    {
        var local = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Local);

        var result = TimestampBounds.AsUtc(local);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void AsUtc_Already_Utc_Is_Unchanged()
    {
        var utc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

        var result = TimestampBounds.AsUtc(utc);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(utc);
        result.Ticks.Should().Be(utc.Ticks);
    }

    [Fact]
    public void AsUtc_Nullable_With_Unspecified_Value_Relabels_Utc()
    {
        DateTime? value = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var result = TimestampBounds.AsUtc(value);

        result.Should().NotBeNull();
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }
}
