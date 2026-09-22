namespace Sifra.Vault.GoogleDrive;

/// <summary>Stand-in Drive blob API client with no network calls — see FakeGoogleDriveEnvelopeApiClient's remarks for the pattern.</summary>
public sealed class FakeGoogleDriveBlobApiClient : IGoogleDriveBlobApiClient
{
    private readonly Dictionary<string, byte[]> _files = new();

    public bool SimulateNetworkFailure { get; set; }
    public bool SimulateUnauthorized { get; set; }

    public Task<bool> ExistsAsync(string fileName, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        return Task.FromResult(_files.ContainsKey(fileName));
    }

    public Task<byte[]?> DownloadAsync(string fileName, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        return Task.FromResult(_files.TryGetValue(fileName, out var content) ? content : null);
    }

    public Task UploadAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        _files[fileName] = content;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string fileName, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        _files.Remove(fileName);
        return Task.CompletedTask;
    }

    private void ThrowIfSimulatingFailure()
    {
        if (SimulateUnauthorized)
        {
            throw new GoogleDriveUnauthorizedException("Simulated unauthorized response.", new InvalidOperationException());
        }
        if (SimulateNetworkFailure)
        {
            throw new GoogleDriveNetworkException("Simulated network failure.", new InvalidOperationException());
        }
    }
}
