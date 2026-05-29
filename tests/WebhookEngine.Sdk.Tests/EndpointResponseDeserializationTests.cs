using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using WebhookEngine.Sdk;

namespace WebhookEngine.Sdk.Tests;

// Regression guard for the SDK <-> API contract drift fixed on
// fix/sdk-contract-parity: EndpointResponse had dropped AllowedIps,
// TransformExpression, TransformEnabled, TransformValidatedAt and typed
// CustomHeadersJson/MetadataJson as Dictionary<string,string> instead of
// JsonElement; EventTypeResponse dropped IdempotencyWindowMinutes; and
// MessageAttemptResponse.RequestHeadersJson was a Dictionary instead of
// JsonElement?. All assertions here drive the REAL WebhookEngineClient so they
// exercise the SDK's own camelCase + case-insensitive JsonSerializerOptions.
public class EndpointResponseDeserializationTests
{
    private static WebhookEngineClient ClientReturning(string json)
    {
        var http = new HttpClient(new StubHttpMessageHandler(json))
        {
            BaseAddress = new Uri("https://engine.test")
        };
        return new WebhookEngineClient(http);
    }

    [Fact]
    public async Task Endpoints_GetAsync_Deserializes_AllowedIps_Transform_Fields_And_Header_JsonElement()
    {
        var endpointId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var json = $$"""
        {
          "data": {
            "id": "{{endpointId}}",
            "appId": "{{appId}}",
            "url": "https://receiver.test/hook",
            "description": "primary",
            "status": "active",
            "customHeadersJson": { "X-A": "1" },
            "secretOverride": null,
            "metadataJson": { "team": "payments" },
            "allowedIps": ["10.0.0.0/8", "192.168.1.1"],
            "transformExpression": "{ id: id }",
            "transformEnabled": true,
            "transformValidatedAt": "2026-05-01T00:00:00Z",
            "filterEventTypes": [],
            "createdAt": "2026-05-01T00:00:00Z",
            "updatedAt": "2026-05-02T00:00:00Z"
          },
          "meta": { "requestId": "req_abc" }
        }
        """;

        var client = ClientReturning(json);

        var result = await client.Endpoints.GetAsync(endpointId);

        result.Should().NotBeNull();
        result!.Status.Should().Be("active");

        result.AllowedIps.Should().HaveCount(2);
        result.AllowedIps.Should().ContainInOrder("10.0.0.0/8", "192.168.1.1");

        result.TransformExpression.Should().Be("{ id: id }");
        result.TransformEnabled.Should().BeTrue();
        result.TransformValidatedAt.Should().Be(
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        result.CustomHeadersJson.ValueKind.Should().Be(JsonValueKind.Object);
        result.CustomHeadersJson.GetProperty("X-A").GetString().Should().Be("1");

        result.MetadataJson.ValueKind.Should().Be(JsonValueKind.Object);
        result.MetadataJson.GetProperty("team").GetString().Should().Be("payments");
    }

    [Fact]
    public async Task EventTypes_GetAsync_Deserializes_IdempotencyWindowMinutes()
    {
        var eventTypeId = Guid.NewGuid();
        var json = $$"""
        {
          "data": {
            "id": "{{eventTypeId}}",
            "name": "order.created",
            "description": "Order created event",
            "schema": null,
            "isArchived": false,
            "idempotencyWindowMinutes": 30,
            "createdAt": "2026-05-01T00:00:00Z"
          },
          "meta": { "requestId": "req_def" }
        }
        """;

        var client = ClientReturning(json);

        var result = await client.EventTypes.GetAsync(eventTypeId);

        result.Should().NotBeNull();
        result!.IdempotencyWindowMinutes.Should().Be(30);
    }

    [Fact]
    public async Task Messages_ListAttemptsAsync_Deserializes_RequestHeadersJson_As_JsonElement_Object()
    {
        var messageId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var json = $$"""
        {
          "data": [
            {
              "id": "{{attemptId}}",
              "messageId": "{{messageId}}",
              "endpointId": "{{endpointId}}",
              "attemptNumber": 1,
              "status": "delivered",
              "statusCode": 200,
              "requestHeadersJson": { "Content-Type": "application/json", "X-B": "2" },
              "responseBody": "ok",
              "error": null,
              "latencyMs": 42,
              "createdAt": "2026-05-01T00:00:00Z"
            }
          ],
          "meta": {
            "requestId": "req_ghi",
            "pagination": { "page": 1, "pageSize": 20, "totalCount": 1 }
          }
        }
        """;

        var client = ClientReturning(json);

        var result = await client.Messages.ListAttemptsAsync(messageId);

        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();
        result.Data!.Should().HaveCount(1);

        var attempt = result.Data![0];
        attempt.RequestHeadersJson.Should().NotBeNull();
        attempt.RequestHeadersJson!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        attempt.RequestHeadersJson.Value
            .GetProperty("Content-Type").GetString().Should().Be("application/json");
        attempt.RequestHeadersJson.Value
            .GetProperty("X-B").GetString().Should().Be("2");
    }

    [Fact]
    public async Task Endpoints_TestAsync_Deserializes_Result_And_Request_Preview()
    {
        var endpointId = Guid.NewGuid();
        var json = """
        {
          "data": {
            "success": true,
            "statusCode": 200,
            "latencyMs": 123,
            "responseBody": "{\"ok\":true}",
            "error": null,
            "request": {
              "url": "https://receiver.test/hook",
              "headers": {
                "webhook-id": "msg_test",
                "Content-Type": "application/json"
              },
              "body": "{\"event\":\"ping\"}"
            }
          },
          "meta": { "requestId": "req_jkl" }
        }
        """;

        var client = ClientReturning(json);

        var result = await client.Endpoints.TestAsync(
            endpointId, new TestEndpointRequest { EventType = "ping" });

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Request.Url.Should().Be("https://receiver.test/hook");
        result.Request.Headers.Should().ContainKey("webhook-id");
        result.Request.Headers["webhook-id"].Should().Be("msg_test");
    }

    [Fact]
    public async Task Endpoints_TestAsync_Sends_Post_To_Test_Path_With_CamelCase_EventType_Body()
    {
        var endpointId = Guid.NewGuid();
        var responseJson = """
        {
          "data": {
            "success": true,
            "statusCode": 200,
            "latencyMs": 1,
            "responseBody": "",
            "error": null,
            "request": { "url": "https://receiver.test/hook", "headers": {}, "body": "" }
          },
          "meta": { "requestId": "req_mno" }
        }
        """;

        var capturing = new CapturingHttpMessageHandler(responseJson);
        var http = new HttpClient(capturing) { BaseAddress = new Uri("https://engine.test") };
        var client = new WebhookEngineClient(http);

        await client.Endpoints.TestAsync(
            endpointId, new TestEndpointRequest { EventType = "order.created" });

        capturing.LastMethod.Should().Be(HttpMethod.Post);
        capturing.LastRequestUri.Should().NotBeNull();
        capturing.LastRequestUri!.AbsolutePath
            .Should().EndWith($"/api/v1/endpoints/{endpointId}/test");
        capturing.LastRequestBody.Should().Contain("\"eventType\":\"order.created\"");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHttpMessageHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _body;

        public CapturingHttpMessageHandler(string body) => _body = body;

        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
