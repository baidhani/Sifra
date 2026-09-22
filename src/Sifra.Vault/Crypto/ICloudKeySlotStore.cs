namespace Sifra.Vault.Crypto;

/// <summary>
/// Generic shape any cloud-storage provider must expose for the shared
/// vault key mechanism (Phase 3): publishing and fetching the wrapped
/// master-password <see cref="VaultMasterKeySlot"/> so a second device can
/// join an existing vault using the same master password. Mirrors
/// ICloudAuthProvider/ICloudSyncProvider's pattern — nothing in
/// VaultDeviceEnrollmentService depends on which real provider (Dropbox,
/// Google Drive, OneDrive) is wired up, or on one being connected at all.
/// The recovery-key slot is never exposed here — recovery and
/// device-joining are deliberately separate concerns.
/// </summary>
public interface ICloudKeySlotStore
{
    bool IsConnected { get; }

    /// <returns>True if a vault has already been published to this cloud account.</returns>
    /// <exception cref="Sync.SyncUnavailableException">The provider is not connected.</exception>
    bool VaultExists();

    /// <returns>The published master-password slot, or null if none exists yet.</returns>
    /// <exception cref="Sync.SyncUnavailableException">The provider is not connected.</exception>
    VaultMasterKeySlot? DownloadMasterPasswordSlot();

    /// <exception cref="Sync.SyncUnavailableException">The provider is not connected.</exception>
    void UploadMasterPasswordSlot(VaultMasterKeySlot slot);
}
