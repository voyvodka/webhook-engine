using Microsoft.Extensions.DependencyInjection;

namespace WebhookEngine.Application;

/// <summary>
/// Registers Application layer services.
/// Placeholder while controller-based flow remains the primary execution path.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        return services;
    }
}
