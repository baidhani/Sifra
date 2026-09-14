using System.IO.Pipes;
using System.Text;
using Sifra.Vault.Pairing;
using Xunit;

namespace Sifra.Vault.Tests;

/// <summary>
/// PipeFramingTests proves the framing logic against a MemoryStream; this
/// proves it over a real NamedPipeServerStream/NamedPipeClientStream pair —
/// the actual IPC mechanism the Desktop&lt;-&gt;NativeHost pairing handshake
/// depends on (see ExtensionPairingServer / Program.cs's PerformPairingAsync).
/// </summary>
public class PipeFramingNamedPipeTests
{
    [Fact]
    public async Task ClientAndServer_RoundTripARequestAndResponse()
    {
        var pipeName = "sifra-pipe-framing-test-" + Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(cts.Token);
            var request = await PipeFraming.ReadFrameAsync(server, cts.Token);
            var requestText = Encoding.UTF8.GetString(request!);
            await PipeFraming.WriteFrameAsync(server, Encoding.UTF8.GetBytes($"echo:{requestText}"), cts.Token);
        });

        await client.ConnectAsync(cts.Token);
        await PipeFraming.WriteFrameAsync(client, Encoding.UTF8.GetBytes("hello"), cts.Token);
        var response = await PipeFraming.ReadFrameAsync(client, cts.Token);

        await serverTask;
        Assert.Equal("echo:hello", Encoding.UTF8.GetString(response!));
    }

    [Fact]
    public async Task Client_ConnectingToNonexistentPipe_TimesOut()
    {
        // Models "Sifra Desktop isn't running" — the native host's own
        // connect attempt in PerformPairingAsync uses the same short-timeout
        // pattern to report that failure almost instantly.
        using var connectCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        using var client = new NamedPipeClientStream(".", "sifra-pipe-that-does-not-exist-" + Guid.NewGuid(), PipeDirection.InOut, PipeOptions.Asynchronous);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(connectCts.Token));
    }
}
