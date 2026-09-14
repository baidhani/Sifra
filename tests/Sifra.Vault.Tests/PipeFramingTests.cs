using System.Text;
using Sifra.Vault.Pairing;
using Xunit;

namespace Sifra.Vault.Tests;

public class PipeFramingTests
{
    [Fact]
    public async Task WriteFrameThenReadFrame_RoundTripsThePayload()
    {
        using var stream = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");

        await PipeFraming.WriteFrameAsync(stream, payload, CancellationToken.None);
        stream.Position = 0;
        var result = await PipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task ReadFrame_OnEmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();

        var result = await PipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadFrame_WhenStreamEndsMidPayload_ReturnsNull()
    {
        using var stream = new MemoryStream();
        // Claims a 100-byte payload but the stream only has 3 bytes of it —
        // models a client disconnecting mid-write.
        await stream.WriteAsync(BitConverter.GetBytes(100));
        await stream.WriteAsync(new byte[] { 1, 2, 3 });
        stream.Position = 0;

        var result = await PipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task WriteFrameThenReadFrame_WithEmptyPayload_RoundTrips()
    {
        using var stream = new MemoryStream();

        await PipeFraming.WriteFrameAsync(stream, Array.Empty<byte>(), CancellationToken.None);
        stream.Position = 0;
        var result = await PipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        Assert.Empty(result!);
    }

    [Fact]
    public async Task ReadFrame_RespectsCancellation()
    {
        using var stream = new MemoryStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PipeFraming.ReadFrameAsync(stream, cts.Token));
    }
}
