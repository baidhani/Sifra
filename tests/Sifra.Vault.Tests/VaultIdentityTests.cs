using Sifra.Vault.Attachments;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Session;
using Sifra.Vault.Sync;
using Sifra.Vault.Tags;
using static Sifra.Vault.Tests.CredentialFieldTestHelpers;

namespace Sifra.Vault.Tests;

/// <summary>
/// Covers the cross-vault sync safety check: VaultSyncService must refuse
/// to merge in a pulled envelope whose VaultId doesn't match this device's
/// own, rather than silently merging in ciphertext it can never decrypt
/// (the exact failure observed live before this check existed — a
/// different throwaway vault's data ended up on the same cloud file and
/// crashed credential list decryption).
/// </summary>
public sealed class VaultIdentityTests : IDisposable
{
    private const string MasterPassword = "correct-horse-battery-staple";

    private readonly string _deviceADirectory;
    private readonly string _deviceBDirectory;
    private readonly LocalFakeVaultEnvelopeCloudStore _cloud = new();

    public VaultIdentityTests()
    {
        _deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-vaultid-a-" + Guid.NewGuid());
        _deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-vaultid-b-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_deviceADirectory)) Directory.Delete(_deviceADirectory, recursive: true);
        if (Directory.Exists(_deviceBDirectory)) Directory.Delete(_deviceBDirectory, recursive: true);
    }

    /// <summary>Fully sets up a vault (VMK bootstrapped via EstablishRecoverySlot, so its VaultId is recorded) at the given directory.</summary>
    private static VaultIdentityStore SetUpVault(string dataDirectory, string masterPassword)
    {
        var vaultService = new VaultService(new VaultStore(dataDirectory));
        var authenticator = new VaultAuthenticator(new VaultAccessCredentialStore(dataDirectory), new FileAccessAuditLog(Path.Combine(dataDirectory, "vault-access.log")));
        var encryption = new VaultEncryptionService(new VaultMasterKeyStore(dataDirectory));
        var identityStore = new VaultIdentityStore(dataDirectory);
        var recoveryKey = vaultService.CreateVault();
        new VaultRecoveryService(vaultService, authenticator, encryption, identityStore: identityStore).EstablishRecoverySlot(recoveryKey, masterPassword);
        return identityStore;
    }

    private VaultSyncService CreateSyncService(string dataDirectory) => new(
        new CredentialStore(dataDirectory), new CredentialTombstoneStore(dataDirectory), new TagStore(dataDirectory),
        new AttachmentStore(dataDirectory), new AttachmentTombstoneStore(dataDirectory), _cloud,
        identityStore: new VaultIdentityStore(dataDirectory));

    [Fact]
    public void EstablishRecoverySlot_RecordsAVaultId()
    {
        var identityStore = SetUpVault(_deviceADirectory, MasterPassword);

        Assert.NotNull(identityStore.LoadVaultId());
    }

    [Fact]
    public void TwoDevicesThatJoinedTheSameVault_ComputeTheIdenticalVaultId()
    {
        var identityStoreA = SetUpVault(_deviceADirectory, MasterPassword);
        var vaultIdA = identityStoreA.LoadVaultId();

        // Device B joins via the shared-vault-key mechanism, sharing the SAME VMK.
        var sharedCloudKeySlots = new LocalFakeCloudKeySlotStore();
        var encryptionA = new VaultEncryptionService(new VaultMasterKeyStore(_deviceADirectory));
        var authenticatorA = new VaultAuthenticator(new VaultAccessCredentialStore(_deviceADirectory), new FileAccessAuditLog(Path.Combine(_deviceADirectory, "vault-access.log")));
        new VaultDeviceEnrollmentService(new VaultMasterKeyStore(_deviceADirectory), encryptionA, authenticatorA, sharedCloudKeySlots).PublishMasterPasswordSlot();

        var identityStoreB = new VaultIdentityStore(_deviceBDirectory);
        var encryptionB = new VaultEncryptionService(new VaultMasterKeyStore(_deviceBDirectory));
        var authenticatorB = new VaultAuthenticator(new VaultAccessCredentialStore(_deviceBDirectory), new FileAccessAuditLog(Path.Combine(_deviceBDirectory, "vault-access.log")));
        new VaultDeviceEnrollmentService(new VaultMasterKeyStore(_deviceBDirectory), encryptionB, authenticatorB, sharedCloudKeySlots, identityStore: identityStoreB)
            .JoinExistingVault(MasterPassword);

        Assert.Equal(vaultIdA, identityStoreB.LoadVaultId());
    }

    [Fact]
    public void TwoUnrelatedVaults_ComputeDifferentVaultIds()
    {
        var identityStoreA = SetUpVault(_deviceADirectory, MasterPassword);
        var identityStoreB = SetUpVault(_deviceBDirectory, "a-totally-different-password");

        Assert.NotEqual(identityStoreA.LoadVaultId(), identityStoreB.LoadVaultId());
    }

    [Fact]
    public void SyncNow_BetweenTwoDevicesOfTheSameVault_Succeeds()
    {
        SetUpVault(_deviceADirectory, MasterPassword);
        SetUpVault(_deviceBDirectory, MasterPassword); // different vaults with coincidentally the same password would still differ in practice (recovery key differs), but this test only exercises the identity check with matching ids forced below

        // Force both devices to the same VaultId, standing in for "these two devices legitimately share a vault" without needing the full join flow again here (covered by the test above).
        var sharedId = new VaultIdentityStore(_deviceADirectory).LoadVaultId()!;
        new VaultIdentityStore(_deviceBDirectory).SaveVaultId(sharedId);

        CreateSyncService(_deviceADirectory).SyncNow();
        var exception = Record.Exception(() => CreateSyncService(_deviceBDirectory).SyncNow());

        Assert.Null(exception);
    }

    [Fact]
    public void SyncNow_WhenThePulledEnvelopeBelongsToADifferentVault_ThrowsAndTouchesNoLocalData()
    {
        SetUpVault(_deviceADirectory, MasterPassword);
        SetUpVault(_deviceBDirectory, "a-totally-different-password"); // a genuinely different, unrelated vault

        CreateSyncService(_deviceADirectory).SyncNow(); // publishes device A's vault to the shared cloud

        var credentialStoreB = new CredentialStore(_deviceBDirectory);
        Assert.Throws<VaultMismatchException>(() => CreateSyncService(_deviceBDirectory).SyncNow());

        // Device B's local data must be completely untouched by the refused sync.
        Assert.Empty(credentialStoreB.GetAll());
    }

    [Fact]
    public void SyncNow_WhenLocalVaultIdIsUnknown_SkipsTheCheckPermissively()
    {
        // No VaultIdentityStore configured locally (identityStore: null,
        // the default) — the check must not block sync just because this
        // side can't verify; it's deliberately permissive, not fail-closed,
        // for devices/tests that predate this feature.
        SetUpVault(_deviceADirectory, MasterPassword);
        var syncA = new VaultSyncService(
            new CredentialStore(_deviceADirectory), new CredentialTombstoneStore(_deviceADirectory), new TagStore(_deviceADirectory),
            new AttachmentStore(_deviceADirectory), new AttachmentTombstoneStore(_deviceADirectory), _cloud); // no identityStore
        syncA.SyncNow();

        var syncBWithNoIdentityStore = new VaultSyncService(
            new CredentialStore(_deviceBDirectory), new CredentialTombstoneStore(_deviceBDirectory), new TagStore(_deviceBDirectory),
            new AttachmentStore(_deviceBDirectory), new AttachmentTombstoneStore(_deviceBDirectory), _cloud); // no identityStore either

        var exception = Record.Exception(() => syncBWithNoIdentityStore.SyncNow());

        Assert.Null(exception);
    }
}
