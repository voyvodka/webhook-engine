using System.Net;
using System.Text;
using FluentAssertions;
using NSubstitute;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.Infrastructure.Tests.Services;

// B6 regression: pins that HttpDeliveryService threads the egress allowlist onto the
// request as AllowedNetworks so the ConnectCallback (Program.cs) can enforce it.
public class HttpDeliveryServiceAllowlistOptionTests
{
    [Fact]
    public async Task DeliverAsync_When_AllowedIpsJson_Configured_Sets_AllowedNetworks_Option()
    {
        var handler = new CapturingHandler();
        var service = new HttpDeliveryService(StubFactory(handler));

        var request = BuildRequest(allowedIpsJson: """["203.0.113.0/24"]""");

        await service.DeliverAsync(request, CancellationToken.None);

        handler.OptionPresent.Should().BeTrue("a non-empty allowlist must reach the ConnectCallback to be enforced");
        handler.CapturedNetworks.Should().ContainSingle()
            .Which.Should().Be(IPNetwork.Parse("203.0.113.0/24"));
    }

    [Fact]
    public async Task DeliverAsync_With_Multiple_Cidrs_Sets_All_Parsed_Networks_In_Order()
    {
        var handler = new CapturingHandler();
        var service = new HttpDeliveryService(StubFactory(handler));

        var request = BuildRequest(allowedIpsJson: """["203.0.113.0/24","198.51.100.0/24"]""");

        await service.DeliverAsync(request, CancellationToken.None);

        handler.OptionPresent.Should().BeTrue();
        handler.CapturedNetworks.Should().Equal(
            IPNetwork.Parse("203.0.113.0/24"),
            IPNetwork.Parse("198.51.100.0/24"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public async Task DeliverAsync_When_AllowedIpsJson_Empty_Or_Absent_Leaves_Option_Unset(string? allowedIpsJson)
    {
        var handler = new CapturingHandler();
        var service = new HttpDeliveryService(StubFactory(handler));

        var request = BuildRequest(allowedIpsJson);

        await service.DeliverAsync(request, CancellationToken.None);

        handler.OptionPresent.Should().BeFalse("no allowlist means unrestricted egress — the option must stay unset");
    }

    private static IHttpClientFactory StubFactory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("webhook-delivery").Returns(_ => new HttpClient(handler));
        return factory;
    }

    private static DeliveryRequest BuildRequest(string? allowedIpsJson) => new()
    {
        MessageId = "msg_1",
        EndpointUrl = "https://receiver.example.test/hook",
        Payload = """{"hello":"world"}""",
        SignedHeaders = new SignedHeaders
        {
            WebhookId = "msg_1",
            WebhookTimestamp = "1700000000",
            WebhookSignature = "v1,dGVzdA=="
        },
        AllowedIpsJson = allowedIpsJson
    };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public bool OptionPresent { get; private set; }
        public IReadOnlyList<IPNetwork>? CapturedNetworks { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Snapshot while the request is alive — DeliverAsync disposes it via `using`
            // the moment SendAsync returns.
            OptionPresent = request.Options.TryGetValue(
                DeliveryHttpRequestOptions.AllowedNetworks, out var networks);
            CapturedNetworks = networks;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
