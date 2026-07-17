namespace WebhookEngine.API.Contracts;

// A naked ?after=2026-07-01 binds as Unspecified, which Npgsql refuses to write
// to a timestamptz parameter (ArgumentException → 500). Treat unspecified as UTC
// and convert local; the filter's meaning is preserved, only the kind changes.
public static class TimestampBounds
{
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? AsUtc(DateTime? value)
        => value is null ? null : AsUtc(value.Value);
}
