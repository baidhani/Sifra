using Sifra.Vault.Audit;
using Sifra.Vault.Auth;

namespace Sifra.Vault.Crypto;

/// <summary>
/// Phase 3: lets a second device join an existing vault using the SAME
/// master password, instead of creating a brand-new vault. The mechanism
/// is deliberately simple, per the product decision: sign in to the same
/// cloud storage account used when the vault was first created, then
/// unlock with the existing master password — no recovery key involved.
///
/// This works by publishing/fetching the wrapped master-password
/// <see cref="VaultMasterKeySlot"/> itself (not the recovery-key slot).
/// The KEK salt travels with the slot, so the same password, on any
/// device, derives the same KEK and unwraps the identical vault master
/// key every other device already has.
/// </summary>
public sealed class VaultDeviceEnrollmentService
{
    private readonly VaultMasterKeyStore _keyStore;
    private readonly VaultEncryptionService _encryption;
    private readonly VaultAuthenticator _authenticator;
    private readonly ICloudKeySlotStore _cloudKeySlotStore;
    private readonly AuditLogger? _auditLogger;
    private readonly VaultIdentityStore? _identityStore;

    /// <param name="identityStore">
    /// Optional (Phase 3). When provided, a successful join records this
    /// vault's identity fingerprint locally — see VaultIdentityStore's
    /// remarks. Because it's a pure function of the now-shared VMK, this
    /// naturally ends up identical to the originating device's, with no
    /// extra synchronization. Null means the cross-vault sync safety check
    /// is simply not available; existing callers and tests are unaffected.
    /// </param>
    public VaultDeviceEnrollmentService(
        VaultMasterKeyStore keyStore,
        VaultEncryptionService encryption,
        VaultAuthenticator authenticator,
        ICloudKeySlotStore cloudKeySlotStore,
        AuditLogger? auditLogger = null,
        VaultIdentityStore? identityStore = null)
    {
        _keyStore = keyStore;
        _encryption = encryption;
        _authenticator = authenticator;
        _cloudKeySlotStore = cloudKeySlotStore;
        _auditLogger = auditLogger;
        _identityStore = identityStore;
    }

    /// <summary>
    /// Checks whether a vault has already been published to the signed-in
    /// cloud account — the signal Setup uses to branch between "create a
    /// new vault" and "join the existing one."
    /// </summary>
    /// <exception cref="Sync.SyncUnavailableException">The cloud provider is not connected.</exception>
    public bool CloudVaultExists() => _cloudKeySlotStore.VaultExists();

    /// <summary>
    /// Publishes this device's current master-password slot to the cloud
    /// account, so a future device can join this vault. Call once right
    /// after a fresh vault's master-password slot is established, and
    /// again any time that slot is re-wrapped (e.g. a master password
    /// change) so every device stays joinable with the latest password.
    /// </summary>
    /// <exception cref="InvalidOperationException">No local master-password slot exists yet.</exception>
    /// <exception cref="Sync.SyncUnavailableException">The cloud provider is not connected.</exception>
    public void PublishMasterPasswordSlot()
    {
        var slot = _keyStore.LoadSlot(VaultEncryptionService.MasterPasswordSlot)
            ?? throw new InvalidOperationException("No master-password slot exists locally to publish.");

        _cloudKeySlotStore.UploadMasterPasswordSlot(slot);

        _auditLogger?.Log(nameof(PublishMasterPasswordSlot), Environment.UserName);
    }

    /// <summary>
    /// Joins an existing vault on this (new) device: downloads the
    /// published master-password slot and unwraps it with the given
    /// password. On success this device now shares the same vault master
    /// key as every other device on the vault, and is unlocked with the
    /// same master password going forward.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No vault has been published to this cloud account, or this device
    /// already has a local vault key.
    /// </exception>
    /// <exception cref="VaultDecryptionFailedException">The password is wrong for this vault.</exception>
    /// <exception cref="Sync.SyncUnavailableException">The cloud provider is not connected.</exception>
    public void JoinExistingVault(string existingMasterPassword)
    {
        if (_keyStore.HasAnySlot())
        {
            throw new InvalidOperationException("This device already has a vault key — cannot join another vault.");
        }

        var slot = _cloudKeySlotStore.DownloadMasterPasswordSlot()
            ?? throw new InvalidOperationException("No vault has been published to this cloud account yet.");

        _keyStore.SaveSlot(VaultEncryptionService.MasterPasswordSlot, slot);

        byte[] vmk;
        try
        {
            vmk = _encryption.DeriveKey(existingMasterPassword, VaultEncryptionService.MasterPasswordSlot);
        }
        catch (VaultDecryptionFailedException)
        {
            // Wrong password — leave this device exactly as it was before the attempt.
            _keyStore.DeleteSlot(VaultEncryptionService.MasterPasswordSlot);
            _auditLogger?.Log(nameof(JoinExistingVault), Environment.UserName, details: "outcome=wrong_password");
            throw;
        }

        _authenticator.SetCredential(existingMasterPassword);
        _identityStore?.SaveVaultId(VaultEncryptionService.ComputeVaultId(vmk));
        _auditLogger?.Log(nameof(JoinExistingVault), Environment.UserName, details: "outcome=success");
    }
}
