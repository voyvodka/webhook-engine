using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Metrics;
using WebhookEngine.Core.Models;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.Worker;

public class DeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeliveryWorker> _logger;
    private readonly DeliveryOptions _deliveryOptions;
    private readonly RetryPolicyOptions _retryPolicy;
    private readonly IEndpointRateLimiter _endpointRateLimiter;
    private readonly WebhookMetrics? _metrics;
    private readonly string _workerId = $"worker_{Environment.MachineName}_{Guid.NewGuid().ToString("N")[..8]}";

    public DeliveryWorker(
        IServiceProvider serviceProvider,
        ILogger<DeliveryWorker> logger,
        IOptions<DeliveryOptions> deliveryOptions,
        IOptions<RetryPolicyOptions> retryPolicy,
        IEndpointRateLimiter endpointRateLimiter,
        WebhookMetrics? metrics = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _deliveryOptions = deliveryOptions.Value;
        _retryPolicy = retryPolicy.Value;
        _endpointRateLimiter = endpointRateLimiter;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeliveryWorker started. WorkerId: {WorkerId}", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var messageQueue = scope.ServiceProvider.GetRequiredService<IMessageQueue>();
                var deliveryService = scope.ServiceProvider.GetRequiredService<IDeliveryService>();
                var signingService = scope.ServiceProvider.GetRequiredService<ISigningService>();
                var healthTracker = scope.ServiceProvider.GetRequiredService<IEndpointHealthTracker>();
                var messageRepo = scope.ServiceProvider.GetRequiredService<MessageRepository>();
                var endpointRepo = scope.ServiceProvider.GetRequiredService<EndpointRepository>();
                var notifier = scope.ServiceProvider.GetService<IDeliveryNotifier>();
                var stateMachine = scope.ServiceProvider.GetRequiredService<IMessageStateMachine>();

                var messages = await messageQueue.DequeueAsync(_deliveryOptions.BatchSize, _workerId, stoppingToken);

                if (messages.Count == 0)
                {
                    await Task.Delay(_deliveryOptions.PollIntervalMs, stoppingToken);
                    continue;
                }

                _metrics?.RecordQueueDequeue(messages.Count);

                foreach (var message in messages)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    await ProcessMessageAsync(message, deliveryService, signingService, healthTracker, messageRepo, endpointRepo, notifier, stateMachine, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — stop dequeuing
                break;
            }
            catch (Exception ex)
            {
                // Never throw from background workers — log and continue
                _logger.LogError(ex, "DeliveryWorker encountered an error");
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("DeliveryWorker stopped. WorkerId: {WorkerId}", _workerId);
    }

    private async Task ProcessMessageAsync(
        Core.Entities.Message message,
        IDeliveryService deliveryService,
        ISigningService signingService,
        IEndpointHealthTracker healthTracker,
        MessageRepository messageRepo,
        EndpointRepository endpointRepo,
        IDeliveryNotifier? notifier,
        IMessageStateMachine stateMachine,
        CancellationToken ct)
    {
        try
        {
            // Check circuit breaker state before delivery
            var circuitState = await healthTracker.GetCircuitStateAsync(message.EndpointId, ct);
            if (circuitState == CircuitState.Open)
            {
                _logger.LogWarning("Circuit open for endpoint {EndpointId}, skipping message {MessageId}", message.EndpointId, message.Id);

                var health = await healthTracker.GetHealthAsync(message.EndpointId, ct);
                var nextTryAt = health?.CooldownUntil ?? DateTime.UtcNow.AddSeconds(30);

                await messageRepo.ReschedulePendingAsync(message.Id, nextTryAt, ct);
                return;
            }

            var endpoint = await endpointRepo.GetByIdAsync(message.AppId, message.EndpointId, ct);
            if (endpoint is null)
            {
                _logger.LogWarning("Endpoint {EndpointId} not found for message {MessageId}", message.EndpointId, message.Id);
                await messageRepo.MarkDeadLetterAsync(message.Id, message.AttemptCount, ct);
                return;
            }

            var rateLimitPerMinute = ResolveRateLimitPerMinute(endpoint.MetadataJson);
            if (rateLimitPerMinute is > 0)
            {
                var limitPerMinute = rateLimitPerMinute.Value;
                if (!_endpointRateLimiter.TryAcquire(endpoint.Id, limitPerMinute, out var retryAtUtc))
                {
                    var nextAttemptAt = retryAtUtc > DateTime.UtcNow ? retryAtUtc : DateTime.UtcNow.AddSeconds(1);
                    await messageRepo.ReschedulePendingAsync(message.Id, nextAttemptAt, ct);

                    _logger.LogInformation(
                        "Rate limit reached for endpoint {EndpointId} (limit: {LimitPerMinute}/min). Message {MessageId} rescheduled to {NextAttemptAt}.",
                        endpoint.Id,
                        limitPerMinute,
                        message.Id,
                        nextAttemptAt);

                    return;
                }
            }

            var signingSecret = endpoint.SecretOverride ?? endpoint.Application?.SigningSecret;
            if (string.IsNullOrWhiteSpace(signingSecret))
            {
                _logger.LogError("Signing secret missing for endpoint {EndpointId}, message {MessageId}", message.EndpointId, message.Id);
                await messageRepo.MarkDeadLetterAsync(message.Id, message.AttemptCount, ct);
                return;
            }

            // Sign the payload
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signedHeaders = signingService.Sign(message.Id.ToString(), timestamp, message.Payload, signingSecret);

            var customHeaders = ParseCustomHeaders(endpoint.CustomHeadersJson);
            var requestHeaders = BuildRequestHeaders(signedHeaders, customHeaders);

            // Build delivery request
            var request = new DeliveryRequest
            {
                MessageId = message.Id.ToString(),
                EndpointUrl = endpoint.Url,
                Payload = message.Payload,
                SignedHeaders = signedHeaders,
                CustomHeaders = customHeaders
            };

            // Deliver
            _metrics?.RecordDeliveryAttempt();
            var result = await deliveryService.DeliverAsync(request, ct);

            var currentAttempt = message.AttemptCount + 1;

            // Record attempt
            var attempt = new Core.Entities.MessageAttempt
            {
                MessageId = message.Id,
                EndpointId = message.EndpointId,
                AttemptNumber = currentAttempt,
                Status = result.Success ? AttemptStatus.Success : (result.Error == "Timeout" ? AttemptStatus.Timeout : AttemptStatus.Failed),
                StatusCode = result.StatusCode,
                RequestHeadersJson = JsonSerializer.Serialize(requestHeaders),
                ResponseBody = result.ResponseBody,
                Error = result.Error,
                LatencyMs = (int)result.LatencyMs
            };
            await messageRepo.CreateAttemptAsync(attempt, ct);

            if (result.Success)
            {
                _metrics?.RecordDeliverySuccess(result.LatencyMs);
                await messageRepo.MarkDeliveredAsync(message.Id, currentAttempt, ct);
                message.Status = MessageStatus.Delivered;
                await healthTracker.RecordSuccessAsync(message.EndpointId, ct);
                _logger.LogInformation("Message {MessageId} delivered to {EndpointId} in {LatencyMs}ms", message.Id, message.EndpointId, result.LatencyMs);

                if (notifier is not null)
                    await notifier.NotifyDeliverySuccessAsync(message.Id, message.EndpointId, currentAttempt, (int)result.LatencyMs, ct);
            }
            else
            {
                _metrics?.RecordDeliveryFailure(result.LatencyMs);
                await healthTracker.RecordFailureAsync(message.EndpointId, ct);

                if (currentAttempt >= message.MaxRetries)
                {
                    _metrics?.RecordDeadLetter();
                    await messageRepo.MarkDeadLetterAsync(message.Id, currentAttempt, ct);
                    message.Status = MessageStatus.DeadLetter;
                    _logger.LogWarning("Message {MessageId} moved to dead letter after {AttemptCount} attempts", message.Id, currentAttempt);

                    if (notifier is not null)
                        await notifier.NotifyDeadLetterAsync(message.Id, message.EndpointId, currentAttempt, ct);
                }
                else
                {
                    _metrics?.RecordRetryScheduled();
                    var nextRetryAt = CalculateNextRetryAt(currentAttempt);
                    await messageRepo.MarkFailedForRetryAsync(message.Id, currentAttempt, nextRetryAt, ct);
                    message.Status = MessageStatus.Failed;
                    _logger.LogWarning(
                        "Message {MessageId} delivery failed (attempt {AttemptCount}). Next retry at {NextRetryAt}. Error: {Error}",
                        message.Id,
                        currentAttempt,
                        nextRetryAt,
                        result.Error ?? $"HTTP {result.StatusCode}");

                    if (notifier is not null)
                        await notifier.NotifyDeliveryFailureAsync(message.Id, message.EndpointId, currentAttempt, result.Error ?? $"HTTP {result.StatusCode}", ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", message.Id);

            // CORR-02: Only transition back to Pending if the current status allows it.
            // Prevents Delivered->Pending and DeadLetter->Pending regression from error recovery.
            // message.Status is updated in-memory after each DB write so this reflects
            // the committed state: if MarkDeliveredAsync succeeded, message.Status is Delivered
            // and IsValid(Delivered, Pending) returns false, blocking the regression.
            if (stateMachine.IsValid(message.Status, MessageStatus.Pending))
            {
                await messageRepo.UpdateMessageStatusAsync(message.Id, MessageStatus.Pending, ct);
            }
            else
            {
                _logger.LogWarning(
                    "Skipping status reset for message {MessageId}: transition {From} -> Pending is invalid",
                    message.Id, message.Status);
            }
        }
    }

    private DateTime CalculateNextRetryAt(int currentAttempt)
    {
        var backoffIndex = Math.Min(currentAttempt - 1, _retryPolicy.BackoffSchedule.Length - 1);
        var backoffSeconds = _retryPolicy.BackoffSchedule[backoffIndex];
        return DateTime.UtcNow.AddSeconds(backoffSeconds);
    }

    private static Dictionary<string, string> ParseCustomHeaders(string? customHeadersJson)
    {
        if (string.IsNullOrWhiteSpace(customHeadersJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(customHeadersJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int? ResolveRateLimitPerMinute(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty("rateLimitPerMinute", out var rateLimitElement))
                return null;

            if (rateLimitElement.ValueKind == JsonValueKind.Number && rateLimitElement.TryGetInt32(out var numericValue))
                return numericValue > 0 ? numericValue : null;

            if (rateLimitElement.ValueKind == JsonValueKind.String
                && int.TryParse(rateLimitElement.GetString(), out var stringValue))
            {
                return stringValue > 0 ? stringValue : null;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> BuildRequestHeaders(
        SignedHeaders signedHeaders,
        Dictionary<string, string> customHeaders)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["webhook-id"] = signedHeaders.WebhookId,
            ["webhook-timestamp"] = signedHeaders.WebhookTimestamp,
            ["webhook-signature"] = signedHeaders.WebhookSignature,
            ["User-Agent"] = "WebhookEngine/1.0"
        };

        foreach (var header in customHeaders)
        {
            headers[header.Key] = header.Value;
        }

        return headers;
    }
}
