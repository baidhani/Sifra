namespace Sifra.Vault.Dropbox;

/// <summary>Stand-in Dropbox blob API client with no network calls — see FakeDropboxEnvelopeApiClient's remarks for the pattern.</summary>
public sealed class FakeDropboxBlobApiClient : IDropboxBlobApiClient
{
    private readonly Dictionary<string, byte[]> _files = new();

    public bool SimulateNetworkFailure { get; set; }
    public bool SimulateUnauthorized { get; set; }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        return Task.FromResult(_files.ContainsKey(path));
    }

    public Task<byte[]?> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        return Task.FromResult(_files.TryGetValue(path, out var content) ? content : null);
    }

    public Task UploadAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        _files[path] = content;
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
            throw new DropboxUnauthorizedException("Simulated unauthorized response.", new InvalidOperationException());
        }
        if (SimulateNetworkFailure)
        {
            throw new DropboxNetworkException("Simulated network failure.", new InvalidOperationException());
        }
    }
}
