namespace Sifra.Vault.PCloud;

/// <summary>
/// Stand-in pCloud API client with no network calls — implements both
/// envelope (hash-conditional) and blob (unconditional) operations against
/// an in-memory dictionary, shared by tests exactly like
/// FakeDropboxEnvelopeApiClient/FakeDropboxBlobApiClient's pattern. The
/// "hash" here is just a monotonically increasing counter per path, since
/// tests only need it to change on every write, not to match pCloud's
/// real hash algorithm.
/// </summary>
public sealed class FakePCloudApiClient : IPCloudEnvelopeApiClient, IPCloudBlobApiClient
{
    private readonly Dictionary<string, (byte[] Content, int Hash)> _files = new();
    private int _nextHash = 1;

    public bool SimulateNetworkFailure { get; set; }
    public bool SimulateUnauthorized { get; set; }

    /// <summary>Directly mutates the file, bypassing hash checks — represents another client's already-succeeded write, for test setup.</summary>
    public void ForceRemoteWrite(string path, byte[] content)
    {
        _files[path] = (content, _nextHash++);
    }

    // ----- Envelope (hash-conditional) -----

    public Task<PCloudDownloadResult?> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        return Task.FromResult(_files.TryGetValue(path, out var entry)
            ? new PCloudDownloadResult(entry.Content, entry.Hash.ToString())
            : null);
    }

    public Task<string?> UploadAsync(string path, byte[] content, string? expectedHash, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();

        var currentHash = _files.TryGetValue(path, out var existing) ? existing.Hash.ToString() : null;
        if (currentHash != expectedHash)
        {
            return Task.FromResult((string?)null);
        }

        var newHash = _nextHash++;
        _files[path] = (content, newHash);
        return Task.FromResult((string?)newHash.ToString());
    }

    // ----- Blob (unconditional) -----

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        return Task.FromResult(_files.ContainsKey(path));
    }

    async Task<byte[]?> IPCloudBlobApiClient.DownloadAsync(string path, CancellationToken cancellationToken)
    {
        var result = await DownloadAsync(path, cancellationToken);
        return result?.Content;
    }

    public Task UploadAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        _files[path] = (content, _nextHash++);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        _files.Remove(path);
        return Task.CompletedTask;
    }

    private void ThrowIfSimulatingFailure()
    {
        if (SimulateUnauthorized)
        {
            throw new PCloudUnauthorizedException("Simulated unauthorized response.", new InvalidOperationException());
        }
        if (SimulateNetworkFailure)
        {
            throw new PCloudNetworkException("Simulated network failure.", new InvalidOperationException());
        }
    }
}
