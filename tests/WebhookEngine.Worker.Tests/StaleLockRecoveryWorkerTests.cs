using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Options;

namespace WebhookEngine.Worker.Tests;

public class StaleLockRecoveryWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_Calls_ReleaseStaleLocks_With_Configured_Duration()
    {
        var queue = Substitute.For<IMessageQueue>();
        queue.ReleaseStaleLocksAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var services = new ServiceCollection()
            .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton(queue)
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILogger<StaleLockRecoveryWorker>>();
        var options = Options.Create(new DeliveryOptions
        {
            StaleLockMinutes = 7
        });

        var worker = new StaleLockRecoveryWorker(services, logger, options);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        await Task.Delay(200);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        await queue.Received(1).ReleaseStaleLocksAsync(
            Arg.Is<TimeSpan>(duration => duration == TimeSpan.FromMinutes(7)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Handles_Queue_Error_Without_Crashing()
    {
        var queue = Substitute.For<IMessageQueue>();
        queue.ReleaseStaleLocksAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("queue error"));

        var services = new ServiceCollection()
            .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton(queue)
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILogger<StaleLockRecoveryWorker>>();
        var options = Options.Create(new DeliveryOptions
        {
            StaleLockMinutes = 5
        });

        var worker = new StaleLockRecoveryWorker(services, logger, options);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        await Task.Delay(200);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        await queue.Received().ReleaseStaleLocksAsync(
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }
}
