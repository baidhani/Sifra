using Sifra.Vault.Auth;
using Sifra.Vault.Sync;

namespace Sifra.Vault.GoogleDrive;

/// <summary>Real ICloudBlobStore implementation for Google Drive — see IGoogleDriveBlobApiClient's remarks.</summary>
public sealed class GoogleDriveCloudBlobStore : ICloudBlobStore
{
    private readonly IGoogleDriveBlobApiClient _apiClient;
    private readonly ICloudAuthProvider _authProvider;
    private readonly TimeSpan _callTimeout;

    public GoogleDriveCloudBlobStore(IGoogleDriveBlobApiClient apiClient, ICloudAuthProvider authProvider, TimeSpan? callTimeout = null)
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
        return _apiClient.ExistsAsync(FileNameFor(blobId), cts.Token).GetAwaiter().GetResult();
    }

    public bool TryDownload(string blobId, out byte[] content)
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        var result = _apiClient.DownloadAsync(FileNameFor(blobId), cts.Token).GetAwaiter().GetResult();
        content = result ?? [];
        return result is not null;
    }

    public void Upload(string blobId, byte[] content)
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        _apiClient.UploadAsync(FileNameFor(blobId), content, cts.Token).GetAwaiter().GetResult();
    }

    public void Delete(string blobId)
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        _apiClient.DeleteAsync(FileNameFor(blobId), cts.Token).GetAwaiter().GetResult();
    }

    private static string FileNameFor(string blobId) => $"sifra-attachment-{blobId}.enc";

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new SyncUnavailableException();
        }
    }
}
