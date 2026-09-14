namespace Sifra.Vault.Pairing;

/// <summary>
/// Shared framing for the Desktop&lt;-&gt;NativeHost pairing pipe: a 4-byte
/// little-endian length prefix followed by that many bytes of UTF-8 JSON —
/// the same convention Sifra.NativeHost already uses for its Chrome
/// native-messaging channel, kept consistent rather than inventing a
/// second framing scheme for the second IPC channel. Works over any
/// Stream, so it's usable (and testable) against a plain MemoryStream as
/// well as a NamedPipeServerStream/NamedPipeClientStream.
/// </summary>
public static class PipeFraming
{
    public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        var lengthBytes = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(lengthBytes, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <returns>The frame's payload, or null if the stream ended before a full frame arrived.</returns>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = await ReadExactlyAsync(stream, 4, cancellationToken);
        if (lengthBytes is null)
        {
            return null;
        }

        var length = BitConverter.ToInt32(lengthBytes, 0);
        return await ReadExactlyAsync(stream, length, cancellationToken);
    }

    private static async Task<byte[]?> ReadExactlyAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
            {
                return null;
            }
            offset += read;
        }
        return buffer;
    }
}
