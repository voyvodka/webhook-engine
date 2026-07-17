namespace WebhookEngine.API.Contracts;

// Uniform clamp for every paginated list action. Without it an unbounded
// ?pageSize=2e9 materializes a whole tenant's table into the single-process
// host, and ?page=0 yields a negative OFFSET → DbException → 500.
public static class PaginationBounds
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Clamp(int page, int pageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
}
