namespace WebhookEngine.API.Contracts;

public record ApiResponse<T>(T Data, ApiMeta Meta);

public record ApiMeta(string RequestId, PaginationMeta? Pagination = null);

public record PaginationMeta(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNext,
    bool HasPrev);

public record ApiError(string Code, string Message, object? Details = null);

public record ApiErrorResponse(ApiError Error, ApiMeta Meta);

public static class ApiEnvelope
{
    public static ApiResponse<T> Success<T>(HttpContext context, T data, PaginationMeta? pagination = null)
        => new(data, Meta(context, pagination));

    public static ApiErrorResponse Error(HttpContext context, string code, string message, object? details = null)
        => new(new ApiError(code, message, details), Meta(context));

    public static ApiMeta Meta(HttpContext context, PaginationMeta? pagination = null)
        => new(RequestId(context), pagination);

    public static PaginationMeta Pagination(int page, int pageSize, int totalCount)
    {
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);
        return new PaginationMeta(
            page,
            pageSize,
            totalCount,
            totalPages,
            HasNext: totalPages > 0 && page < totalPages,
            HasPrev: page > 1);
    }

    private static string RequestId(HttpContext context)
    {
        var requestId = context.Items["RequestId"]?.ToString() ?? "unknown";
        return $"req_{requestId}";
    }
}
