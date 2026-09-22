namespace Sifra.Vault.GoogleDrive;

/// <summary>
/// Stand-in Drive API client with no network calls — models the same
/// client-side check-then-write semantics RealGoogleDriveEnvelopeApiClient
/// actually has (not a stronger, atomic guarantee it doesn't really
/// provide), so tests against this fake reflect the real limitation.
/// </summary>
public sealed class FakeGoogleDriveEnvelopeApiClient : IGoogleDriveEnvelopeApiClient
{
    private byte[]? _content;
    private int _revision;

    public bool SimulateNetworkFailure { get; set; }
    public bool SimulateUnauthorized { get; set; }
    public int UploadAttempts { get; private set; }

    /// <summary>Fires exactly once, the next time DownloadAsync is called, then clears itself — simulates another device's write landing mid-sync.</summary>
    public Action? ConcurrentWriteDuringNextDownload { get; set; }

    /// <summary>Directly mutates the remote, bypassing the revision check — represents another device's already-succeeded write, for test setup.</summary>
    public void ForceRemoteWrite(byte[] content)
    {
        _content = content;
        _revision++;
    }

    public Task<GoogleDriveDownloadResult?> DownloadAsync(string fileName, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();

        var result = _content is null ? null : new GoogleDriveDownloadResult(_content, _revision.ToString());

        var hook = ConcurrentWriteDuringNextDownload;
        ConcurrentWriteDuringNextDownload = null;
        hook?.Invoke();

        return Task.FromResult(result);
    }

    public Task<string?> UploadAsync(string fileName, byte[] content, string? expectedRevision, CancellationToken cancellationToken)
    {
        ThrowIfSimulatingFailure();
        UploadAttempts++;

        var currentRevision = _content is null ? null : _revision.ToString();
        if (currentRevision != expectedRevision)
        {
            return Task.FromResult<string?>(null);
        }

        _content = content;
        _revision++;
        return Task.FromResult<string?>(_revision.ToString());
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
