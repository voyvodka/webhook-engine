using Microsoft.Extensions.DependencyInjection;

namespace WebhookEngine.Application;

/// <summary>
/// Registers Application layer services (MediatR, FluentValidation, AutoMapper).
/// Called from API Program.cs.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        return services;
    }
}
