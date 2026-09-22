using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.Dropbox;
using Sifra.Vault.Session;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class DropboxCloudKeySlotStoreTests
{
    private static (DropboxCloudKeySlotStore Store, FakeDropboxBlobApiClient ApiClient) CreateConnectedStore()
    {
        var apiClient = new FakeDropboxBlobApiClient();
        var auth = new LocalFakeCloudAuthProvider();
        auth.SignIn("someone@example.com");
        return (new DropboxCloudKeySlotStore(apiClient, auth), apiClient);
    }

    [Fact]
    public void VaultExists_ForAnAccountNeverPublishedTo_ReturnsFalse()
    {
        var (store, _) = CreateConnectedStore();

        Assert.False(store.VaultExists());
    }

    [Fact]
    public void UploadThenDownload_RoundTripsTheSameSlot()
    {
        var (store, _) = CreateConnectedStore();
        var slot = new VaultMasterKeySlot("salt-base64", "wrapped-key-base64");

        store.UploadMasterPasswordSlot(slot);

        Assert.True(store.VaultExists());
        var downloaded = store.DownloadMasterPasswordSlot();
        Assert.NotNull(downloaded);
        Assert.Equal(slot.KekSaltBase64, downloaded!.KekSaltBase64);
        Assert.Equal(slot.WrappedKeyBase64, downloaded.WrappedKeyBase64);
    }

    [Fact]
    public void VaultExists_WhenNotConnected_ThrowsSyncUnavailable()
    {
        var apiClient = new FakeDropboxBlobApiClient();
        var auth = new LocalFakeCloudAuthProvider(); // never signed in
        var store = new DropboxCloudKeySlotStore(apiClient, auth);

        Assert.Throws<SyncUnavailableException>(() => store.VaultExists());
    }

    [Fact]
    public void Upload_WhenTheApiClientFailsForNetworkReasons_PropagatesTheDropboxException()
    {
        var (store, apiClient) = CreateConnectedStore();
        apiClient.SimulateNetworkFailure = true;

        Assert.Throws<DropboxNetworkException>(() => store.UploadMasterPasswordSlot(new VaultMasterKeySlot("s", "w")));
    }

    /// <summary>
    /// The end-to-end scenario this store exists for: a fresh device, with
    /// no local vault at all, recovers full access to an existing vault
    /// using nothing but a Dropbox sign-in and the master password — no
    /// synced folder involved. Mirrors
    /// AttachmentBlobSyncStoreTests' two-device pattern for the API sync
    /// path, but for the key-slot/join path.
    /// </summary>
    [Fact]
    public void VaultDeviceEnrollment_EndToEnd_TwoDevicesSharingOneFakeDropboxApiClient_SharesTheSameVmk()
    {
        var apiClient = new FakeDropboxBlobApiClient();
        var authA = new LocalFakeCloudAuthProvider();
        authA.SignIn("someone@example.com");
        var authB = new LocalFakeCloudAuthProvider();
        authB.SignIn("someone@example.com");

        var deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-dropbox-keyslot-a-" + Guid.NewGuid());
        var deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-dropbox-keyslot-b-" + Guid.NewGuid());
        try
        {
            const string masterPassword = "correct-horse-battery-staple";

            var keyStoreA = new VaultMasterKeyStore(deviceADirectory);
            var encryptionA = new VaultEncryptionService(keyStoreA);
            var authenticatorA = new VaultAuthenticator(new VaultAccessCredentialStore(deviceADirectory), new FileAccessAuditLog(Path.Combine(deviceADirectory, "access.log")));
            var vaultServiceA = new VaultService(new VaultStore(deviceADirectory), new AuditLogger(new FileAuditLogSink(Path.Combine(deviceADirectory, "ops.log")), new LocalFakeAdminAlertSink()));
            var recoveryServiceA = new VaultRecoveryService(vaultServiceA, authenticatorA, encryptionA);

            var recoveryKey = vaultServiceA.CreateVault();
            recoveryServiceA.EstablishRecoverySlot(recoveryKey, masterPassword);

            var enrollmentA = new VaultDeviceEnrollmentService(keyStoreA, encryptionA, authenticatorA, new DropboxCloudKeySlotStore(apiClient, authA));
            enrollmentA.PublishMasterPasswordSlot();

            // Device B: brand-new install, no local vault key at all.
            var keyStoreB = new VaultMasterKeyStore(deviceBDirectory);
            var encryptionB = new VaultEncryptionService(keyStoreB);
            var authenticatorB = new VaultAuthenticator(new VaultAccessCredentialStore(deviceBDirectory), new FileAccessAuditLog(Path.Combine(deviceBDirectory, "access.log")));
            var enrollmentB = new VaultDeviceEnrollmentService(keyStoreB, encryptionB, authenticatorB, new DropboxCloudKeySlotStore(apiClient, authB));

            Assert.True(enrollmentB.CloudVaultExists());
            enrollmentB.JoinExistingVault(masterPassword);

            var vmkA = encryptionA.DeriveKey(masterPassword);
            var vmkB = encryptionB.DeriveKey(masterPassword);
            Assert.Equal(vmkA, vmkB);
        }
        finally
        {
            if (Directory.Exists(deviceADirectory)) Directory.Delete(deviceADirectory, recursive: true);
            if (Directory.Exists(deviceBDirectory)) Directory.Delete(deviceBDirectory, recursive: true);
        }
    }

    [Fact]
    public void VaultDeviceEnrollment_JoinWithWrongPassword_LeavesTheNewDeviceWithNoLocalSlot()
    {
        var apiClient = new FakeDropboxBlobApiClient();
        var authA = new LocalFakeCloudAuthProvider();
        authA.SignIn("someone@example.com");
        var authB = new LocalFakeCloudAuthProvider();
        authB.SignIn("someone@example.com");

        var deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-dropbox-keyslot-wrongpw-a-" + Guid.NewGuid());
        var deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-dropbox-keyslot-wrongpw-b-" + Guid.NewGuid());
        try
        {
            var keyStoreA = new VaultMasterKeyStore(deviceADirectory);
            var encryptionA = new VaultEncryptionService(keyStoreA);
            var authenticatorA = new VaultAuthenticator(new VaultAccessCredentialStore(deviceADirectory), new FileAccessAuditLog(Path.Combine(deviceADirectory, "access.log")));
            var vaultServiceA = new VaultService(new VaultStore(deviceADirectory), new AuditLogger(new FileAuditLogSink(Path.Combine(deviceADirectory, "ops.log")), new LocalFakeAdminAlertSink()));
            var recoveryServiceA = new VaultRecoveryService(vaultServiceA, authenticatorA, encryptionA);
            var recoveryKey = vaultServiceA.CreateVault();
            recoveryServiceA.EstablishRecoverySlot(recoveryKey, "correct-horse-battery-staple");

            var enrollmentA = new VaultDeviceEnrollmentService(keyStoreA, encryptionA, authenticatorA, new DropboxCloudKeySlotStore(apiClient, authA));
            enrollmentA.PublishMasterPasswordSlot();

            var keyStoreB = new VaultMasterKeyStore(deviceBDirectory);
            var encryptionB = new VaultEncryptionService(keyStoreB);
            var authenticatorB = new VaultAuthenticator(new VaultAccessCredentialStore(deviceBDirectory), new FileAccessAuditLog(Path.Combine(deviceBDirectory, "access.log")));
            var enrollmentB = new VaultDeviceEnrollmentService(keyStoreB, encryptionB, authenticatorB, new DropboxCloudKeySlotStore(apiClient, authB));

            Assert.Throws<VaultDecryptionFailedException>(() => enrollmentB.JoinExistingVault("wrong-password"));
            Assert.False(keyStoreB.HasAnySlot());
        }
        finally
        {
            if (Directory.Exists(deviceADirectory)) Directory.Delete(deviceADirectory, recursive: true);
            if (Directory.Exists(deviceBDirectory)) Directory.Delete(deviceBDirectory, recursive: true);
        }
    }
}
