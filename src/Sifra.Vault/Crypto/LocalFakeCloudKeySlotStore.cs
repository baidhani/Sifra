using Sifra.Vault.Sync;

namespace Sifra.Vault.Crypto;

/// <summary>
/// Stand-in cloud key-slot store with no network calls — this repo has no
/// real cloud provider wired to the shared-vault-key mechanism yet. Lets
/// tests simulate multiple devices sharing one cloud account by pointing
/// two VaultDeviceEnrollmentService instances at the same instance of this
/// class, and lets tests force a disconnected/unavailable provider.
/// </summary>
public sealed class LocalFakeCloudKeySlotStore : ICloudKeySlotStore
{
    private VaultMasterKeySlot? _publishedSlot;

    public bool IsConnected { get; set; } = true;

    public bool VaultExists()
    {
        EnsureConnected();
        return _publishedSlot is not null;
    }

    public VaultMasterKeySlot? DownloadMasterPasswordSlot()
    {
        EnsureConnected();
        return _publishedSlot;
    }

    public void UploadMasterPasswordSlot(VaultMasterKeySlot slot)
    {
        EnsureConnected();
        _publishedSlot = slot;
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new SyncUnavailableException();
        }
    }
}
