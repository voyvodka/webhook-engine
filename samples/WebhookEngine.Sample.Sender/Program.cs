// ---------------------------------------------------------------
// WebhookEngine Sample — Sender (SDK Demo)
// ---------------------------------------------------------------
// Prerequisites:
//   1. WebhookEngine API running at http://localhost:5100
//   2. An application created via the dashboard (you need its API key)
//
// Usage:
//   dotnet run -- <api-key>
//   dotnet run -- whe_abc123_your-api-key-here
//
// This sample demonstrates:
//   - Creating an event type
//   - Creating an endpoint (pointed at the Sample.Receiver)
//   - Sending a webhook message
//   - Polling for delivery status
// ---------------------------------------------------------------

using WebhookEngine.Sdk;

const string DefaultReceiverUrl = "http://localhost:5200/webhook";

// --- Parse args ---
var apiKey = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("WEBHOOKENGINE_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Usage: dotnet run -- <api-key>");
    Console.Error.WriteLine("  or set WEBHOOKENGINE_API_KEY environment variable.");
    return 1;
}

var baseUrl = Environment.GetEnvironmentVariable("WEBHOOKENGINE_BASE_URL") ?? "http://localhost:5100";
var receiverUrl = Environment.GetEnvironmentVariable("WEBHOOKENGINE_RECEIVER_URL") ?? DefaultReceiverUrl;

Console.WriteLine($"WebhookEngine Sender Sample");
Console.WriteLine($"API:      {baseUrl}");
Console.WriteLine($"Receiver: {receiverUrl}");
Console.WriteLine(new string('-', 50));

using var client = new WebhookEngineClient(apiKey, baseUrl);

try
{
    // 1. Create an event type
    Console.WriteLine("\n[1/4] Creating event type 'order.created'...");
    var eventType = await client.EventTypes.CreateAsync(new CreateEventTypeRequest
    {
        Name = "order.created",
        Description = "Fired when a new order is placed"
    });
    Console.WriteLine($"  -> Event type created: {eventType!.Id} ({eventType.Name})");

    // 2. Create an endpoint pointed at the receiver
    Console.WriteLine("\n[2/4] Creating endpoint...");
    var endpoint = await client.Endpoints.CreateAsync(new CreateEndpointRequest
    {
        Url = receiverUrl,
        Description = "Sample receiver endpoint",
        FilterEventTypes = [eventType.Id]
    });
    Console.WriteLine($"  -> Endpoint created: {endpoint!.Id}");
    Console.WriteLine($"     URL: {endpoint.Url}");

    // 3. Send a webhook message
    Console.WriteLine("\n[3/4] Sending webhook...");
    var sendResult = await client.Messages.SendAsync(new SendMessageRequest
    {
        EventType = "order.created",
        Payload = new
        {
            orderId = "ORD-2026-001",
            customerId = "cust_abc123",
            amount = 149.99,
            currency = "USD",
            items = new[]
            {
                new { sku = "WIDGET-01", name = "Premium Widget", quantity = 2, price = 49.99 },
                new { sku = "GADGET-05", name = "Smart Gadget", quantity = 1, price = 50.01 }
            },
            createdAt = DateTime.UtcNow.ToString("O")
        },
        IdempotencyKey = $"sample-{Guid.NewGuid():N}"
    });
    Console.WriteLine($"  -> Message accepted! Endpoint count: {sendResult!.EndpointCount}");
    Console.WriteLine($"     Message IDs: {string.Join(", ", sendResult.MessageIds)}");

    // 4. Wait and check delivery status
    Console.WriteLine("\n[4/4] Waiting 3 seconds for delivery...");
    await Task.Delay(3000);

    foreach (var msgIdStr in sendResult.MessageIds)
    {
        if (!Guid.TryParse(msgIdStr, out var msgId))
        {
            Console.WriteLine($"  -> Skipping invalid message ID: {msgIdStr}");
            continue;
        }

        var message = await client.Messages.GetAsync(msgId);
        if (message is null)
        {
            Console.WriteLine($"  -> Message {msgIdStr}: not found");
            continue;
        }

        Console.WriteLine($"  -> Message {msgIdStr}: status={message.Status}, attempts={message.AttemptCount}");

        // Show delivery attempts
        var attemptsResponse = await client.Messages.ListAttemptsAsync(msgId);
        if (attemptsResponse.Data is { Count: > 0 })
        {
            foreach (var attempt in attemptsResponse.Data)
            {
                Console.WriteLine($"     Attempt #{attempt.AttemptNumber}: status={attempt.Status}, " +
                                  $"httpStatus={attempt.StatusCode}, latency={attempt.LatencyMs}ms");
            }
        }
    }

    Console.WriteLine("\nDone! Check the receiver console for the delivered webhook.");
    return 0;
}
catch (WebhookEngineException ex)
{
    Console.Error.WriteLine($"\nAPI Error: {ex.Message}");
    Console.Error.WriteLine($"Status: {ex.StatusCode}");
    if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
        Console.Error.WriteLine($"Body: {ex.ResponseBody}");
    return 1;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"\nConnection Error: {ex.Message}");
    Console.Error.WriteLine("Make sure WebhookEngine API is running.");
    return 1;
}
