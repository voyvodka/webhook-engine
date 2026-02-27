using System.Diagnostics.Metrics;

namespace WebhookEngine.Core.Metrics;

/// <summary>
/// Custom Prometheus metrics for webhook delivery operations.
/// Uses System.Diagnostics.Metrics (IMeterFactory compatible).
/// </summary>
public sealed class WebhookMetrics
{
    public const string MeterName = "WebhookEngine";

    private readonly Counter<long> _messagesEnqueued;
    private readonly Counter<long> _deliveriesTotal;
    private readonly Counter<long> _deliveriesSuccess;
    private readonly Counter<long> _deliveriesFailed;
    private readonly Counter<long> _deadLetterTotal;
    private readonly Counter<long> _retriesScheduled;
    private readonly Counter<long> _circuitOpened;
    private readonly Counter<long> _circuitClosed;
    private readonly Counter<long> _staleLockRecovered;
    private readonly Histogram<double> _deliveryDurationMs;
    private readonly UpDownCounter<long> _queueDepth;

    public WebhookMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _messagesEnqueued = meter.CreateCounter<long>(
            "webhookengine.messages.enqueued",
            unit: "{message}",
            description: "Total messages enqueued for delivery");

        _deliveriesTotal = meter.CreateCounter<long>(
            "webhookengine.deliveries.total",
            unit: "{delivery}",
            description: "Total delivery attempts");

        _deliveriesSuccess = meter.CreateCounter<long>(
            "webhookengine.deliveries.success",
            unit: "{delivery}",
            description: "Successful deliveries");

        _deliveriesFailed = meter.CreateCounter<long>(
            "webhookengine.deliveries.failed",
            unit: "{delivery}",
            description: "Failed deliveries");

        _deadLetterTotal = meter.CreateCounter<long>(
            "webhookengine.deadletter.total",
            unit: "{message}",
            description: "Messages moved to dead letter");

        _retriesScheduled = meter.CreateCounter<long>(
            "webhookengine.retries.scheduled",
            unit: "{retry}",
            description: "Retry attempts scheduled");

        _circuitOpened = meter.CreateCounter<long>(
            "webhookengine.circuit.opened",
            unit: "{event}",
            description: "Circuit breaker open events");

        _circuitClosed = meter.CreateCounter<long>(
            "webhookengine.circuit.closed",
            unit: "{event}",
            description: "Circuit breaker close events");

        _staleLockRecovered = meter.CreateCounter<long>(
            "webhookengine.stalelock.recovered",
            unit: "{message}",
            description: "Messages recovered from stale locks");

        _deliveryDurationMs = meter.CreateHistogram<double>(
            "webhookengine.delivery.duration",
            unit: "ms",
            description: "Delivery attempt duration in milliseconds");

        _queueDepth = meter.CreateUpDownCounter<long>(
            "webhookengine.queue.depth",
            unit: "{message}",
            description: "Approximate queue depth (enqueue increments, dequeue decrements)");
    }

    public void RecordMessageEnqueued(int count = 1) => _messagesEnqueued.Add(count);

    public void RecordDeliveryAttempt() => _deliveriesTotal.Add(1);

    public void RecordDeliverySuccess(double durationMs)
    {
        _deliveriesSuccess.Add(1);
        _deliveryDurationMs.Record(durationMs);
    }

    public void RecordDeliveryFailure(double durationMs)
    {
        _deliveriesFailed.Add(1);
        _deliveryDurationMs.Record(durationMs);
    }

    public void RecordDeadLetter() => _deadLetterTotal.Add(1);

    public void RecordRetryScheduled(int count = 1) => _retriesScheduled.Add(count);

    public void RecordCircuitOpened() => _circuitOpened.Add(1);

    public void RecordCircuitClosed() => _circuitClosed.Add(1);

    public void RecordStaleLockRecovered(int count) => _staleLockRecovered.Add(count);

    public void RecordQueueEnqueue(int count = 1) => _queueDepth.Add(count);

    public void RecordQueueDequeue(int count) => _queueDepth.Add(-count);
}
