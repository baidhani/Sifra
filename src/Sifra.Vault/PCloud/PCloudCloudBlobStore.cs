using Sifra.Vault.Auth;
using Sifra.Vault.Sync;

namespace Sifra.Vault.PCloud;

/// <summary>Real ICloudBlobStore implementation for pCloud — see IPCloudBlobApiClient's remarks.</summary>
public sealed class PCloudCloudBlobStore : ICloudBlobStore
{
    private readonly IPCloudBlobApiClient _apiClient;
    private readonly ICloudAuthProvider _authProvider;
    private readonly TimeSpan _callTimeout;

    public PCloudCloudBlobStore(IPCloudBlobApiClient apiClient, ICloudAuthProvider authProvider, TimeSpan? callTimeout = null)
    {
        _apiClient = apiClient;
        _authProvider = authProvider;
        _callTimeout = callTimeout ?? TimeSpan.FromSeconds(30);
    }

    public bool IsConnected => _authProvider.IsAuthenticated;

    public bool Exists(string blobId)
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        return _apiClient.ExistsAsync(PathFor(blobId), cts.Token).GetAwaiter().GetResult();
    }

    public bool TryDownload(string blobId, out byte[] content)
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        var result = _apiClient.DownloadAsync(PathFor(blobId), cts.Token).GetAwaiter().GetResult();
        content = result ?? [];
        return result is not null;
    }

    public void Upload(string blobId, byte[] content)
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        _apiClient.UploadAsync(PathFor(blobId), content, cts.Token).GetAwaiter().GetResult();
    }

    public void Delete(string blobId)
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        _apiClient.DeleteAsync(PathFor(blobId), cts.Token).GetAwaiter().GetResult();
    }

    // A flat filename at the app-folder root rather than a real "/attachments"
    // subfolder — pCloud's app-folder root always exists, but a subfolder
    // would need an explicit createfolder call first (not yet implemented),
    // so this sidesteps that entirely, same convention GoogleDriveCloudBlobStore
    // already uses for the same reason (Drive files aren't addressed by path).
    private static string PathFor(string blobId) => $"/sifra-attachment-{blobId}.enc";

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new SyncUnavailableException();
        }
    }
}
