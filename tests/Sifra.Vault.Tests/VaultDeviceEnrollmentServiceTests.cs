using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Session;
using Sifra.Vault.Sync;
using static Sifra.Vault.Tests.CredentialFieldTestHelpers;

namespace Sifra.Vault.Tests;

public sealed class VaultDeviceEnrollmentServiceTests : IDisposable
{
    private const string MasterPassword = "correct-horse-battery-staple";

    private readonly string _deviceADirectory;
    private readonly string _deviceBDirectory;
    private readonly LocalFakeCloudKeySlotStore _sharedCloud = new();

    public VaultDeviceEnrollmentServiceTests()
    {
        _deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-enroll-a-" + Guid.NewGuid());
        _deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-enroll-b-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_deviceADirectory)) Directory.Delete(_deviceADirectory, recursive: true);
        if (Directory.Exists(_deviceBDirectory)) Directory.Delete(_deviceBDirectory, recursive: true);
    }

    private static VaultAuthenticator Authenticator(string dataDirectory) =>
        new(new VaultAccessCredentialStore(dataDirectory), new FileAccessAuditLog(Path.Combine(dataDirectory, "vault-access.log")));

    private static VaultEncryptionService Encryption(string dataDirectory) => new(new VaultMasterKeyStore(dataDirectory));

    private static CredentialService Credentials(string dataDirectory, VaultEncryptionService encryption) =>
        new(new CredentialStore(dataDirectory), encryption, new FakeCredentialClipboard());

    /// <summary>Sets up "Device A": a fresh vault with one credential, its master-password slot published to the shared fake cloud.</summary>
    private VaultDeviceEnrollmentService SetUpDeviceAWithOneCredential()
    {
        var vaultService = new VaultService(new VaultStore(_deviceADirectory));
        var authenticator = Authenticator(_deviceADirectory);
        var encryption = Encryption(_deviceADirectory);
        var recoveryKey = vaultService.CreateVault();
        new VaultRecoveryService(vaultService, authenticator, encryption).EstablishRecoverySlot(recoveryKey, MasterPassword);
        Credentials(_deviceADirectory, encryption).Add(MasterPassword, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        var enrollment = new VaultDeviceEnrollmentService(new VaultMasterKeyStore(_deviceADirectory), encryption, authenticator, _sharedCloud);
        enrollment.PublishMasterPasswordSlot();
        return enrollment;
    }

    [Fact]
    public void CloudVaultExists_BeforeAnyPublish_ReturnsFalse()
    {
        var enrollment = new VaultDeviceEnrollmentService(
            new VaultMasterKeyStore(_deviceBDirectory), Encryption(_deviceBDirectory), Authenticator(_deviceBDirectory), _sharedCloud);

        Assert.False(enrollment.CloudVaultExists());
    }

    [Fact]
    public void PublishMasterPasswordSlot_MakesCloudVaultExistTrueForAnotherDevice()
    {
        SetUpDeviceAWithOneCredential();

        var deviceB = new VaultDeviceEnrollmentService(
            new VaultMasterKeyStore(_deviceBDirectory), Encryption(_deviceBDirectory), Authenticator(_deviceBDirectory), _sharedCloud);

        Assert.True(deviceB.CloudVaultExists());
    }

    [Fact]
    public void JoinExistingVault_WithTheCorrectPassword_UnlocksTheSameCredentialsAsTheOriginatingDevice()
    {
        // Acceptance: the whole point of the mechanism — a second device,
        // signed into the same cloud account, unlocks with the SAME master
        // password and reaches the SAME vault master key (and therefore the
        // same existing credentials), with no recovery key involved.
        SetUpDeviceAWithOneCredential();

        var deviceBAuthenticator = Authenticator(_deviceBDirectory);
        var deviceBEncryption = Encryption(_deviceBDirectory);
        var deviceB = new VaultDeviceEnrollmentService(
            new VaultMasterKeyStore(_deviceBDirectory), deviceBEncryption, deviceBAuthenticator, _sharedCloud);

        deviceB.JoinExistingVault(MasterPassword);

        Assert.True(deviceBAuthenticator.Authenticate(MasterPassword));

        // Device B has no local credential rows of its own here (Phase 3's
        // credential-content sync is separate work) — this test proves the
        // key-sharing mechanism itself: the SAME VMK is now reachable.
        var deviceAVmk = deviceBEncryption.DeriveKey(MasterPassword);
        var deviceACredential = new CredentialStore(_deviceADirectory).GetAll().Single();
        var passwordField = deviceACredential.Fields.First(f => f.Type == CustomFieldType.Password);
        Assert.Equal("hunter2", Encryption(_deviceADirectory).Decrypt(passwordField.EncryptedValueBase64, deviceAVmk));
    }

    [Fact]
    public void JoinExistingVault_WithTheWrongPassword_ThrowsAndLeavesTheDeviceCleanForRetry()
    {
        // Failure path: wrong password must be rejected cleanly, and must
        // not leave a half-adopted slot that would block a correct retry.
        SetUpDeviceAWithOneCredential();

        var deviceB = new VaultDeviceEnrollmentService(
            new VaultMasterKeyStore(_deviceBDirectory), Encryption(_deviceBDirectory), Authenticator(_deviceBDirectory), _sharedCloud);

        Assert.Throws<VaultDecryptionFailedException>(() => deviceB.JoinExistingVault("wrong-password"));

        // Retry with the correct password must still work — no leftover state blocking it.
        deviceB.JoinExistingVault(MasterPassword);
        Assert.True(Authenticator(_deviceBDirectory).Authenticate(MasterPassword));
    }

    [Fact]
    public void JoinExistingVault_WhenNoVaultHasBeenPublished_ThrowsInvalidOperation()
    {
        var deviceB = new VaultDeviceEnrollmentService(
            new VaultMasterKeyStore(_deviceBDirectory), Encryption(_deviceBDirectory), Authenticator(_deviceBDirectory), _sharedCloud);

        Assert.Throws<InvalidOperationException>(() => deviceB.JoinExistingVault(MasterPassword));
    }

    [Fact]
    public void JoinExistingVault_WhenThisDeviceAlreadyHasAVault_ThrowsInsteadOfOverwritingIt()
    {
        SetUpDeviceAWithOneCredential(); // publishes a vault to the shared cloud

        // Device A itself already has a local vault key — joining (even the same vault) must be rejected.
        var deviceAEnrollment = new VaultDeviceEnrollmentService(
            new VaultMasterKeyStore(_deviceADirectory), Encryption(_deviceADirectory), Authenticator(_deviceADirectory), _sharedCloud);

        Assert.Throws<InvalidOperationException>(() => deviceAEnrollment.JoinExistingVault(MasterPassword));
    }

    [Fact]
    public void PublishMasterPasswordSlot_WhenNoLocalSlotExistsYet_ThrowsInvalidOperation()
    {
        var enrollment = new VaultDeviceEnrollmentService(
            new VaultMasterKeyStore(_deviceADirectory), Encryption(_deviceADirectory), Authenticator(_deviceADirectory), _sharedCloud);

        Assert.Throws<InvalidOperationException>(() => enrollment.PublishMasterPasswordSlot());
    }

    [Fact]
    public void CloudVaultExists_WhenTheCloudProviderIsDisconnected_ThrowsSyncUnavailable()
    {
        _sharedCloud.IsConnected = false;
        var enrollment = new VaultDeviceEnrollmentService(
            new VaultMasterKeyStore(_deviceADirectory), Encryption(_deviceADirectory), Authenticator(_deviceADirectory), _sharedCloud);

        Assert.Throws<SyncUnavailableException>(() => enrollment.CloudVaultExists());
    }

    [Fact]
    public void RecoveryKeySlot_IsNeverPublishedOrExposedThroughEnrollment()
    {
        // Explicit product requirement: joining a device must never route
        // through the recovery key. Proven by the fact that ICloudKeySlotStore
        // only ever carries the master-password slot's shape — there is no
        // API surface here through which a recovery key could travel.
        SetUpDeviceAWithOneCredential();

        var published = _sharedCloud.DownloadMasterPasswordSlot();
        var localMasterSlot = new VaultMasterKeyStore(_deviceADirectory).LoadSlot(VaultEncryptionService.MasterPasswordSlot);
        var localRecoverySlot = new VaultMasterKeyStore(_deviceADirectory).LoadSlot(VaultEncryptionService.RecoveryKeySlot);

        Assert.Equal(localMasterSlot, published);
        Assert.NotEqual(localRecoverySlot!.WrappedKeyBase64, published!.WrappedKeyBase64);
    }
}
