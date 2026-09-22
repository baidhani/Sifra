using System.Text.Json;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Dropbox;

/// <summary>
/// Real ICloudKeySlotStore implementation for Dropbox — the OAuth-API-based
/// replacement FileCloudKeySlotStore's own remarks said was coming
/// eventually. Publishes the wrapped master-password VaultMasterKeySlot to
/// a fixed path in the SAME Dropbox account already used for
/// DropboxVaultEnvelopeCloudStore, so a brand-new device can recover this
/// vault via Dropbox sign-in alone — no desktop-sync folder involved.
/// Reuses IDropboxBlobApiClient (the same client DropboxCloudBlobStore uses
/// for attachments) at a top-level path rather than under /attachments,
/// since this is a single small JSON file, not a per-item blob collection.
/// </summary>
public sealed class DropboxCloudKeySlotStore : ICloudKeySlotStore
{
    private const string KeySlotPath = "/sifra-vault-keyslot.json";

    private readonly IDropboxBlobApiClient _apiClient;
    private readonly ICloudAuthProvider _authProvider;
    private readonly TimeSpan _callTimeout;

    public DropboxCloudKeySlotStore(IDropboxBlobApiClient apiClient, ICloudAuthProvider authProvider, TimeSpan? callTimeout = null)
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
        return _apiClient.ExistsAsync(KeySlotPath, cts.Token).GetAwaiter().GetResult();
    }

    public VaultMasterKeySlot? DownloadMasterPasswordSlot()
    {
        EnsureConnected();
        using var cts = new CancellationTokenSource(_callTimeout);
        var content = _apiClient.DownloadAsync(KeySlotPath, cts.Token).GetAwaiter().GetResult();
        if (content is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<VaultMasterKeySlot>(content)
            ?? throw new DropboxSyncFailedException("The key slot on Dropbox was empty or malformed.", new JsonException());
    }

    public void UploadMasterPasswordSlot(VaultMasterKeySlot slot)
    {
        EnsureConnected();
        var content = JsonSerializer.SerializeToUtf8Bytes(slot);
        using var cts = new CancellationTokenSource(_callTimeout);
        _apiClient.UploadAsync(KeySlotPath, content, cts.Token).GetAwaiter().GetResult();
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new SyncUnavailableException();
        }
    }
}
