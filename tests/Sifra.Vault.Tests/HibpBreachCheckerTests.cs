using System.Net;
using Sifra.Vault.Health;

namespace Sifra.Vault.Tests;

public sealed class HibpBreachCheckerTests
{
    // SHA-1("password123") = CBFDAC6008F9CAB4083784CBD1874F76618D2A97
    // prefix = CBFDA, suffix = C6008F9CAB4083784CBD1874F76618D2A97
    private const string Password = "password123";
    private const string Suffix = "C6008F9CAB4083784CBD1874F76618D2A97";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;
        public int CallCount { get; private set; }

        public StubHandler(params Func<HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpResponseMessage>>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var factory = _responses.Count > 0 ? _responses.Dequeue() : _responses.Last();
            return Task.FromResult(factory());
        }
    }

    [Fact]
    public async Task CheckAsync_WhenSuffixIsInTheResponse_ReturnsBreachedWithCount()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"0000000000000000000000000000000000:1\r\n{Suffix}:42\r\nFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:3"),
        });
        var checker = new HibpBreachChecker(new HttpClient(handler));

        var result = await checker.CheckAsync(Password, CancellationToken.None);

        Assert.Equal(BreachCheckOutcome.Breached, result.Outcome);
        Assert.Equal(42, result.BreachCount);
    }

    [Fact]
    public async Task CheckAsync_WhenSuffixIsNotInTheResponse_ReturnsNotBreached()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("0000000000000000000000000000000000:1"),
        });
        var checker = new HibpBreachChecker(new HttpClient(handler));

        var result = await checker.CheckAsync(Password, CancellationToken.None);

        Assert.Equal(BreachCheckOutcome.NotBreached, result.Outcome);
    }

    [Fact]
    public async Task CheckAsync_NeverSendsTheFullHashOrPlaintext_OnlyTheFiveCharPrefix()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new CapturingStubHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        });
        var checker = new HibpBreachChecker(new HttpClient(handler));

        await checker.CheckAsync(Password, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.EndsWith("/range/CBFDA", capturedRequest!.RequestUri!.ToString());
        Assert.DoesNotContain(Password, capturedRequest.RequestUri.ToString());
        Assert.DoesNotContain(Suffix, capturedRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task CheckAsync_OnATransientServerError_RetriesThenSucceeds()
    {
        var attempt = 0;
        var handler = new CapturingStubHandler(_ =>
        {
            attempt++;
            return attempt < 2
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{Suffix}:5") };
        });
        var checker = new HibpBreachChecker(new HttpClient(handler));

        var result = await checker.CheckAsync(Password, CancellationToken.None);

        Assert.Equal(BreachCheckOutcome.Breached, result.Outcome);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task CheckAsync_WhenTheServerNeverRecovers_ReturnsCheckUnavailableRatherThanNotBreached()
    {
        // Failure path: "false positives in breach detection" — a network
        // failure must never be reported as a clean "not breached" result.
        var handler = new CapturingStubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var checker = new HibpBreachChecker(new HttpClient(handler));

        var result = await checker.CheckAsync(Password, CancellationToken.None);

        Assert.Equal(BreachCheckOutcome.CheckUnavailable, result.Outcome);
    }

    [Fact]
    public async Task CheckAsync_OnANonTransientClientError_ReturnsCheckUnavailableWithoutRetrying()
    {
        var handler = new CapturingStubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var checker = new HibpBreachChecker(new HttpClient(handler));

        var result = await checker.CheckAsync(Password, CancellationToken.None);

        Assert.Equal(BreachCheckOutcome.CheckUnavailable, result.Outcome);
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class CapturingStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int CallCount { get; private set; }

        public CapturingStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responder(request));
        }
    }
}
