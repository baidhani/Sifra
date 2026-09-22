using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class GoogleDriveVaultEnvelopeCloudStoreTests
{
    private static VaultSyncEnvelope EmptyEnvelope() => new([], [], [], [], []);

    private static (GoogleDriveVaultEnvelopeCloudStore Store, FakeGoogleDriveEnvelopeApiClient ApiClient) CreateConnectedStore()
    {
        var apiClient = new FakeGoogleDriveEnvelopeApiClient();
        var auth = new LocalFakeCloudAuthProvider();
        auth.SignIn("someone@example.com");
        return (new GoogleDriveVaultEnvelopeCloudStore(apiClient, auth), apiClient);
    }

    [Fact]
    public void Pull_WhenNothingHasEverBeenPublished_ReturnsNull()
    {
        var (store, _) = CreateConnectedStore();

        Assert.Null(store.Pull());
    }

    [Fact]
    public void TryPush_FirstEverPush_SucceedsWithNoExpectedRevision()
    {
        var (store, _) = CreateConnectedStore();

        var succeeded = store.TryPush(EmptyEnvelope(), expectedRevision: null, out var newRevision);

        Assert.True(succeeded);
        Assert.False(string.IsNullOrEmpty(newRevision));
    }

    [Fact]
    public void Pull_AfterAPush_ReturnsTheSameEnvelopeAndARevision()
    {
        var (store, _) = CreateConnectedStore();
        var credential = new Credential("cred-1", "GitHub", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var pushed = new VaultSyncEnvelope([credential], [], [], [], []);
        store.TryPush(pushed, null, out _);

        var pulled = store.Pull();

        Assert.NotNull(pulled);
        Assert.Single(pulled!.Value.Envelope.Credentials);
        Assert.Equal("cred-1", pulled.Value.Envelope.Credentials[0].Id);
    }

    [Fact]
    public void TryPush_WhenTheRemoteChangedSinceThePulledRevision_ReturnsFalseRatherThanOverwriting()
    {
        var (store, apiClient) = CreateConnectedStore();
        store.TryPush(EmptyEnvelope(), null, out var firstRevision);
        apiClient.ForceRemoteWrite(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(EmptyEnvelope())); // simulates another device's push

        var succeeded = store.TryPush(EmptyEnvelope(), expectedRevision: firstRevision, out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void TryPush_SecondEverPush_RejectedIfCallerStillThinksNothingExistsYet()
    {
        var (store, _) = CreateConnectedStore();
        store.TryPush(EmptyEnvelope(), null, out _);

        var succeeded = store.TryPush(EmptyEnvelope(), expectedRevision: null, out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void Pull_WhenNotConnected_ThrowsSyncUnavailable()
    {
        var apiClient = new FakeGoogleDriveEnvelopeApiClient();
        var auth = new LocalFakeCloudAuthProvider(); // never signed in
        var store = new GoogleDriveVaultEnvelopeCloudStore(apiClient, auth);

        Assert.Throws<SyncUnavailableException>(() => store.Pull());
    }

    [Fact]
    public void Pull_WhenTheApiClientReportsUnauthorized_PropagatesTheGoogleDriveException()
    {
        var (store, apiClient) = CreateConnectedStore();
        apiClient.SimulateUnauthorized = true;

        Assert.Throws<GoogleDriveUnauthorizedException>(() => store.Pull());
    }

    [Fact]
    public void Pull_WhenTheApiClientFailsForNetworkReasons_PropagatesTheGoogleDriveException()
    {
        var (store, apiClient) = CreateConnectedStore();
        apiClient.SimulateNetworkFailure = true;

        Assert.Throws<GoogleDriveNetworkException>(() => store.Pull());
    }

    [Fact]
    public void VaultSyncService_EndToEnd_TwoDevicesSharingOneFakeGoogleDriveApiClient_SyncCorrectly()
    {
        // Proves GoogleDriveVaultEnvelopeCloudStore genuinely satisfies
        // IVaultEnvelopeCloudStore's contract by running the real
        // VaultSyncService against it, not just this store's own methods.
        var apiClient = new FakeGoogleDriveEnvelopeApiClient();
        var authA = new LocalFakeCloudAuthProvider();
        authA.SignIn("someone@example.com");
        var authB = new LocalFakeCloudAuthProvider();
        authB.SignIn("someone@example.com");

        var deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-gdrive-a-" + Guid.NewGuid());
        var deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-gdrive-b-" + Guid.NewGuid());
        try
        {
            var credentialsA = new CredentialService(
                new CredentialStore(deviceADirectory), new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(deviceADirectory)),
                new FakeCredentialClipboard());
            var id = credentialsA.Add("correct-horse-battery-staple", "GitHub", CredentialFieldTestHelpers.LoginFields("firas", "hunter2", "https://github.com"));

            var syncA = new VaultSyncService(
                new CredentialStore(deviceADirectory), new CredentialTombstoneStore(deviceADirectory), new Sifra.Vault.Tags.TagStore(deviceADirectory),
                new Sifra.Vault.Attachments.AttachmentStore(deviceADirectory), new AttachmentTombstoneStore(deviceADirectory),
                new GoogleDriveVaultEnvelopeCloudStore(apiClient, authA));
            var syncB = new VaultSyncService(
                new CredentialStore(deviceBDirectory), new CredentialTombstoneStore(deviceBDirectory), new Sifra.Vault.Tags.TagStore(deviceBDirectory),
                new Sifra.Vault.Attachments.AttachmentStore(deviceBDirectory), new AttachmentTombstoneStore(deviceBDirectory),
                new GoogleDriveVaultEnvelopeCloudStore(apiClient, authB));

            syncA.SyncNow();
            syncB.SyncNow();

            var onB = new CredentialStore(deviceBDirectory).GetAll();
            Assert.Single(onB);
            Assert.Equal(id, onB[0].Id);
        }
        finally
        {
            if (Directory.Exists(deviceADirectory)) Directory.Delete(deviceADirectory, recursive: true);
            if (Directory.Exists(deviceBDirectory)) Directory.Delete(deviceBDirectory, recursive: true);
        }
    }
}
