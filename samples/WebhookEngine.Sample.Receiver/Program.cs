// ---------------------------------------------------------------
// WebhookEngine Sample — Receiver (Webhook Consumer)
// ---------------------------------------------------------------
// This minimal API receives webhooks from WebhookEngine,
// verifies the HMAC-SHA256 signature, and logs the payload.
//
// Usage:
//   dotnet run
//   (Listens on http://localhost:5200 by default)
//
// Environment variables:
//   WEBHOOK_SECRET  — The signing secret for your application
//                     (found in the dashboard or application settings)
//
// Signature verification follows the Standard Webhooks spec:
//   Header: webhook-signature = "v1,<base64>"
//   Signed content: "{webhook-id}.{webhook-timestamp}.{body}"
// ---------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5200");

var app = builder.Build();

app.MapPost("/webhook", async (HttpContext context) =>
{
    // Read headers
    var webhookId = context.Request.Headers["webhook-id"].FirstOrDefault();
    var webhookTimestamp = context.Request.Headers["webhook-timestamp"].FirstOrDefault();
    var webhookSignature = context.Request.Headers["webhook-signature"].FirstOrDefault();

    // Read body
    context.Request.EnableBuffering();
    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
    var body = await reader.ReadToEndAsync();

    // Log the received webhook
    var separator = new string('=', 60);
    Console.WriteLine();
    Console.WriteLine(separator);
    Console.WriteLine($"  WEBHOOK RECEIVED  [{DateTime.UtcNow:HH:mm:ss.fff}]");
    Console.WriteLine(separator);
    Console.WriteLine($"  webhook-id:        {webhookId ?? "(missing)"}");
    Console.WriteLine($"  webhook-timestamp: {webhookTimestamp ?? "(missing)"}");
    Console.WriteLine($"  webhook-signature: {webhookSignature ?? "(missing)"}");
    Console.WriteLine($"  content-type:      {context.Request.ContentType}");
    Console.WriteLine($"  body length:       {body.Length} bytes");
    Console.WriteLine();

    // Pretty-print payload (best effort)
    try
    {
        var jsonDoc = System.Text.Json.JsonDocument.Parse(body);
        var prettyBody = System.Text.Json.JsonSerializer.Serialize(
            jsonDoc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("  Payload:");
        foreach (var line in prettyBody.Split('\n'))
            Console.WriteLine($"    {line}");
    }
    catch
    {
        Console.WriteLine($"  Payload (raw): {body}");
    }

    // Verify signature
    var secret = Environment.GetEnvironmentVariable("WEBHOOK_SECRET");
    if (string.IsNullOrWhiteSpace(secret))
    {
        Console.WriteLine();
        Console.WriteLine("  [WARN] WEBHOOK_SECRET not set — skipping signature verification.");
        Console.WriteLine("         Set it to enable HMAC-SHA256 verification.");
        Console.WriteLine(separator);
        return Results.Ok(new { status = "received", verified = false });
    }

    if (string.IsNullOrEmpty(webhookId) || string.IsNullOrEmpty(webhookTimestamp) || string.IsNullOrEmpty(webhookSignature))
    {
        Console.WriteLine("  [FAIL] Missing required webhook headers.");
        Console.WriteLine(separator);
        return Results.BadRequest(new { error = "Missing webhook headers" });
    }

    // Check timestamp tolerance (5 minutes)
    if (long.TryParse(webhookTimestamp, out var ts))
    {
        var messageTime = DateTimeOffset.FromUnixTimeSeconds(ts);
        var drift = Math.Abs((DateTimeOffset.UtcNow - messageTime).TotalMinutes);
        if (drift > 5)
        {
            Console.WriteLine($"  [FAIL] Timestamp too old/new (drift: {drift:F1} min).");
            Console.WriteLine(separator);
            return Results.BadRequest(new { error = "Timestamp out of tolerance" });
        }
    }

    // Compute expected signature
    var signedContent = $"{webhookId}.{webhookTimestamp}.{body}";
    var secretBytes = ResolveSecretBytes(secret);
    using var hmac = new HMACSHA256(secretBytes);
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedContent));
    var expectedSignature = $"v1,{Convert.ToBase64String(hash)}";

    // Compare — webhook-signature can contain multiple signatures (v1,sig1 v1,sig2)
    var signatures = webhookSignature.Split(' ');
    var verified = signatures.Any(s => string.Equals(s.Trim(), expectedSignature, StringComparison.Ordinal));

    Console.WriteLine();
    if (verified)
    {
        Console.WriteLine("  [OK] Signature verified successfully.");
    }
    else
    {
        Console.WriteLine("  [FAIL] Signature mismatch!");
        Console.WriteLine($"         Expected: {expectedSignature}");
        Console.WriteLine($"         Received: {webhookSignature}");
    }
    Console.WriteLine(separator);

    return verified
        ? Results.Ok(new { status = "received", verified = true })
        : Results.Unauthorized();
});

app.MapGet("/", () => Results.Ok(new
{
    service = "WebhookEngine Sample Receiver",
    status = "running",
    endpoint = "POST /webhook"
}));

Console.WriteLine("WebhookEngine Sample Receiver");
Console.WriteLine("Listening on http://localhost:5200");
Console.WriteLine("Webhook endpoint: POST http://localhost:5200/webhook");
Console.WriteLine("Waiting for webhooks...\n");

app.Run();

// --- Helper: Resolve secret bytes (matches HmacSigningService logic) ---
static byte[] ResolveSecretBytes(string secret)
{
    if (secret.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase))
        return Encoding.UTF8.GetBytes(secret);

    try
    {
        return Convert.FromBase64String(secret);
    }
    catch (FormatException)
    {
        return Encoding.UTF8.GetBytes(secret);
    }
}
