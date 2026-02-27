using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebhookEngine.Application;

namespace WebhookEngine.Application.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddApplicationServices_Registers_MediatR()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();

        // MediatR registration should add IMediator
        var mediatorDescriptor = services.FirstOrDefault(
            d => d.ServiceType.Name == "IMediator");

        mediatorDescriptor.Should().NotBeNull(
            "AddApplicationServices should register MediatR's IMediator");
    }

    [Fact]
    public void AddApplicationServices_Returns_Same_ServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddApplicationServices();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddApplicationServices_Can_Be_Called_Multiple_Times_Without_Error()
    {
        var services = new ServiceCollection();

        var act = () =>
        {
            services.AddApplicationServices();
            services.AddApplicationServices();
        };

        act.Should().NotThrow();
    }
}
