using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.PCloud;
using Sifra.Vault.Session;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class PCloudCloudKeySlotStoreTests
{
    private static (PCloudCloudKeySlotStore Store, FakePCloudApiClient ApiClient) CreateConnectedStore()
    {
        var apiClient = new FakePCloudApiClient();
        var auth = new LocalFakeCloudAuthProvider();
        auth.SignIn("someone@example.com");
        return (new PCloudCloudKeySlotStore(apiClient, auth), apiClient);
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
        var apiClient = new FakePCloudApiClient();
        var auth = new LocalFakeCloudAuthProvider(); // never signed in
        var store = new PCloudCloudKeySlotStore(apiClient, auth);

        Assert.Throws<SyncUnavailableException>(() => store.VaultExists());
    }

    [Fact]
    public void Upload_WhenTheApiClientFailsForNetworkReasons_PropagatesThePCloudException()
    {
        var (store, apiClient) = CreateConnectedStore();
        apiClient.SimulateNetworkFailure = true;

        Assert.Throws<PCloudNetworkException>(() => store.UploadMasterPasswordSlot(new VaultMasterKeySlot("s", "w")));
    }

    /// <summary>See DropboxCloudKeySlotStoreTests' equivalent for the scenario this proves.</summary>
    [Fact]
    public void VaultDeviceEnrollment_EndToEnd_TwoDevicesSharingOneFakePCloudApiClient_SharesTheSameVmk()
    {
        var apiClient = new FakePCloudApiClient();
        var authA = new LocalFakeCloudAuthProvider();
        authA.SignIn("someone@example.com");
        var authB = new LocalFakeCloudAuthProvider();
        authB.SignIn("someone@example.com");

        var deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-pcloud-keyslot-a-" + Guid.NewGuid());
        var deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-pcloud-keyslot-b-" + Guid.NewGuid());
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

            var enrollmentA = new VaultDeviceEnrollmentService(keyStoreA, encryptionA, authenticatorA, new PCloudCloudKeySlotStore(apiClient, authA));
            enrollmentA.PublishMasterPasswordSlot();

            var keyStoreB = new VaultMasterKeyStore(deviceBDirectory);
            var encryptionB = new VaultEncryptionService(keyStoreB);
            var authenticatorB = new VaultAuthenticator(new VaultAccessCredentialStore(deviceBDirectory), new FileAccessAuditLog(Path.Combine(deviceBDirectory, "access.log")));
            var enrollmentB = new VaultDeviceEnrollmentService(keyStoreB, encryptionB, authenticatorB, new PCloudCloudKeySlotStore(apiClient, authB));

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
}
