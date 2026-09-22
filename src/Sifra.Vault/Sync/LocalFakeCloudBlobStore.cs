namespace Sifra.Vault.Sync;

/// <summary>
/// Stand-in cloud blob store with no network calls — this repo has no
/// real provider wired to attachment blob sync yet. Two AttachmentBlobSyncService
/// instances pointed at the same instance of this class simulate two
/// devices sharing one cloud account, same pattern as the other Local Fake
/// stores in this namespace.
/// </summary>
public sealed class LocalFakeCloudBlobStore : ICloudBlobStore
{
    private readonly Dictionary<string, byte[]> _blobs = new();

    public bool IsConnected { get; set; } = true;

    public bool Exists(string blobId)
    {
        EnsureConnected();
        return _blobs.ContainsKey(blobId);
    }

    public bool TryDownload(string blobId, out byte[] content)
    {
        EnsureConnected();
        return _blobs.TryGetValue(blobId, out content!);
    }

    public void Upload(string blobId, byte[] content)
    {
        EnsureConnected();
        _blobs[blobId] = content;
    }

    public void Delete(string blobId)
    {
        EnsureConnected();
        _blobs.Remove(blobId);
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new SyncUnavailableException();
        }
    }
}
