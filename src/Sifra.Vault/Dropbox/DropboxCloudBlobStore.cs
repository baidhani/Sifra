using Sifra.Vault.Auth;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Dropbox;

/// <summary>Real ICloudBlobStore implementation for Dropbox — see IDropboxBlobApiClient's remarks.</summary>
public sealed class DropboxCloudBlobStore : ICloudBlobStore
{
    private const string BlobFolder = "/attachments";

    private readonly IDropboxBlobApiClient _apiClient;
    private readonly ICloudAuthProvider _authProvider;
    private readonly TimeSpan _callTimeout;

    public DropboxCloudBlobStore(IDropboxBlobApiClient apiClient, ICloudAuthProvider authProvider, TimeSpan? callTimeout = null)
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

    private static string PathFor(string blobId) => $"{BlobFolder}/{blobId}.enc";

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new SyncUnavailableException();
        }
    }
}
