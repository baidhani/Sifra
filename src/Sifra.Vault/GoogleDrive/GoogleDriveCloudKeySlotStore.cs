using System.Text.Json;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;

namespace Sifra.Vault.GoogleDrive;

/// <summary>
/// Real ICloudKeySlotStore implementation for Google Drive — see
/// DropboxCloudKeySlotStore's remarks; same role, same file-naming
/// convention as GoogleDriveCloudBlobStore (a fixed file name Drive looks
/// up by name within the app's own Drive folder, since Drive has no path
/// concept the way Dropbox does).
/// </summary>
public sealed class GoogleDriveCloudKeySlotStore : ICloudKeySlotStore
{
    private const string KeySlotFileName = "sifra-vault-keyslot.json";

    private readonly IGoogleDriveBlobApiClient _apiClient;
    private readonly ICloudAuthProvider _authProvider;
    private readonly TimeSpan _callTimeout;

    public GoogleDriveCloudKeySlotStore(IGoogleDriveBlobApiClient apiClient, ICloudAuthProvider authProvider, TimeSpan? callTimeout = null)
    {
        _apiClient = apiClient;
        _authProvider = authProvider;
        _callTimeout = callTimeout ?? TimeSpan.FromSeconds(30);
    }

    public bool IsConnected => _authProvider.IsAuthenticated;

    public bool VaultExists()
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        return _apiClient.ExistsAsync(KeySlotFileName, cts.Token).GetAwaiter().GetResult();
    }

    public VaultMasterKeySlot? DownloadMasterPasswordSlot()
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        var content = _apiClient.DownloadAsync(KeySlotFileName, cts.Token).GetAwaiter().GetResult();
        if (content is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<VaultMasterKeySlot>(content)
            ?? throw new GoogleDriveSyncFailedException("The key slot on Google Drive was empty or malformed.", new JsonException());
    }

    public void UploadMasterPasswordSlot(VaultMasterKeySlot slot)
    {
        EnsureConnected();
        var content = JsonSerializer.SerializeToUtf8Bytes(slot);
        using var cts = new CancellationTokenSource(_callTimeout);
        _apiClient.UploadAsync(KeySlotFileName, content, cts.Token).GetAwaiter().GetResult();
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new SyncUnavailableException();
        }
    }
}
