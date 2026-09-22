using System.Text.Json;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;

namespace Sifra.Vault.PCloud;

/// <summary>Real ICloudKeySlotStore implementation for pCloud — see DropboxCloudKeySlotStore's remarks; same role, same fixed-path convention as PCloudVaultEnvelopeCloudStore.</summary>
public sealed class PCloudCloudKeySlotStore : ICloudKeySlotStore
{
    private const string KeySlotPath = "/sifra-vault-keyslot.json";

    private readonly IPCloudBlobApiClient _apiClient;
    private readonly ICloudAuthProvider _authProvider;
    private readonly TimeSpan _callTimeout;

    public PCloudCloudKeySlotStore(IPCloudBlobApiClient apiClient, ICloudAuthProvider authProvider, TimeSpan? callTimeout = null)
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
            ?? throw new PCloudSyncFailedException("The key slot on pCloud was empty or malformed.", new JsonException());
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
