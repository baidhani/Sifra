namespace Sifra.Vault.Dropbox;

/// <summary>
/// Stand-in Dropbox API client with no network calls — models a single
/// file's content + revision, and the same Add/Update write-mode
/// semantics the real Dropbox API enforces (Add fails if the file already
/// exists; Update fails if the file's current rev no longer matches).
/// Lets DropboxVaultEnvelopeCloudStore be tested without ever touching the
/// real Dropbox API or needing a Dropbox account.
/// </summary>
public sealed class FakeDropboxEnvelopeApiClient : IDropboxEnvelopeApiClient
{
    private readonly Dictionary<string, (byte[] Content, int Rev)> _files = new();

    public bool SimulateNetworkFailure { get; set; }
    public bool SimulateUnauthorized { get; set; }
    public int UploadAttempts { get; private set; }

    /// <summary>Fires exactly once, the next time DownloadAsync is called, then clears itself — simulates another device's write landing mid-sync.</summary>
    public Action? ConcurrentWriteDuringNextDownload { get; set; }

    /// <summary>Directly mutates the file, bypassing write-mode checks — represents another client's already-succeeded write, for test setup.</summary>
    public void ForceRemoteWrite(string path, byte[] content)
    {
        var currentRev = _files.TryGetValue(path, out var existing) ? existing.Rev : 0;
        _files[path] = (content, currentRev + 1);
    }

    public Task<DropboxDownloadResult?> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();

        var result = _files.TryGetValue(path, out var file) ? new DropboxDownloadResult(file.Content, file.Rev.ToString()) : null;

        var hook = ConcurrentWriteDuringNextDownload;
        ConcurrentWriteDuringNextDownload = null;
        hook?.Invoke();

        return Task.FromResult(result);
    }

    public Task<string?> UploadAsync(string path, byte[] content, string? expectedRev, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        UploadAttempts++;

        var currentRev = _files.TryGetValue(path, out var existing) ? existing.Rev.ToString() : null;
        if (currentRev != expectedRev)
        {
            return Task.FromResult<string?>(null); // Add-mode-exists or Update-mode-rev-mismatch, same rejection either way
        }

        var newRev = (existing.Rev) + 1;
        _files[path] = (content, newRev);
        return Task.FromResult<string?>(newRev.ToString());
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
