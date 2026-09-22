using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Dropbox;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class DropboxVaultEnvelopeCloudStoreTests
{
    private static VaultSyncEnvelope EmptyEnvelope() => new([], [], [], [], []);

    private static (DropboxVaultEnvelopeCloudStore Store, FakeDropboxEnvelopeApiClient ApiClient, LocalFakeCloudAuthProvider Auth) CreateConnectedStore()
    {
        var apiClient = new FakeDropboxEnvelopeApiClient();
        var auth = new LocalFakeCloudAuthProvider();
        auth.SignIn("someone@example.com");
        return (new DropboxVaultEnvelopeCloudStore(apiClient, auth), apiClient, auth);
    }

    [Fact]
    public void Pull_WhenNothingHasEverBeenPublished_ReturnsNull()
    {
        var (store, _, _) = CreateConnectedStore();

        Assert.Null(store.Pull());
    }

    [Fact]
    public void TryPush_FirstEverPush_SucceedsWithNoExpectedRevision()
    {
        var (store, _, _) = CreateConnectedStore();

        var succeeded = store.TryPush(EmptyEnvelope(), expectedRevision: null, out var newRevision);

        Assert.True(succeeded);
        Assert.False(string.IsNullOrEmpty(newRevision));
    }

    [Fact]
    public void Pull_AfterAPush_ReturnsTheSameEnvelopeAndARevision()
    {
        var (store, _, _) = CreateConnectedStore();
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
        var (store, apiClient, _) = CreateConnectedStore();
        store.TryPush(EmptyEnvelope(), null, out var firstRevision);
        apiClient.ForceRemoteWrite("/sifra-vault-envelope.json", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(EmptyEnvelope())); // simulates another device's push

        var succeeded = store.TryPush(EmptyEnvelope(), expectedRevision: firstRevision, out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void TryPush_SecondEverPush_RejectedIfCallerStillThinksNothingExistsYet()
    {
        // Mirrors real Dropbox Add-mode semantics: expectedRevision:null
        // means "nothing published yet," so it must be rejected once
        // something actually has been.
        var (store, _, _) = CreateConnectedStore();
        store.TryPush(EmptyEnvelope(), null, out _);

        var succeeded = store.TryPush(EmptyEnvelope(), expectedRevision: null, out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void Pull_WhenNotConnected_ThrowsSyncUnavailable()
    {
        var apiClient = new FakeDropboxEnvelopeApiClient();
        var auth = new LocalFakeCloudAuthProvider(); // never signed in
        var store = new DropboxVaultEnvelopeCloudStore(apiClient, auth);

        Assert.Throws<SyncUnavailableException>(() => store.Pull());
    }

    [Fact]
    public void Pull_WhenTheApiClientReportsUnauthorized_PropagatesTheDropboxException()
    {
        var (store, apiClient, _) = CreateConnectedStore();
        apiClient.SimulateUnauthorized = true;

        Assert.Throws<DropboxUnauthorizedException>(() => store.Pull());
    }

    [Fact]
    public void Pull_WhenTheApiClientFailsForNetworkReasons_PropagatesTheDropboxException()
    {
        var (store, apiClient, _) = CreateConnectedStore();
        apiClient.SimulateNetworkFailure = true;

        Assert.Throws<DropboxNetworkException>(() => store.Pull());
    }

    [Fact]
    public void VaultSyncService_EndToEnd_TwoDevicesSharingOneFakeDropboxApiClient_SyncCorrectly()
    {
        // Proves DropboxVaultEnvelopeCloudStore genuinely satisfies
        // IVaultEnvelopeCloudStore's contract by running the real
        // VaultSyncService against it, not just this store's own methods
        // in isolation.
        var apiClient = new FakeDropboxEnvelopeApiClient();
        var authA = new LocalFakeCloudAuthProvider();
        authA.SignIn("someone@example.com");
        var authB = new LocalFakeCloudAuthProvider();
        authB.SignIn("someone@example.com");

        var deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-dropbox-a-" + Guid.NewGuid());
        var deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-dropbox-b-" + Guid.NewGuid());
        try
        {
            var credentialsA = new CredentialService(
                new CredentialStore(deviceADirectory), new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(deviceADirectory)),
                new FakeCredentialClipboard());
            var id = credentialsA.Add("correct-horse-battery-staple", "GitHub", CredentialFieldTestHelpers.LoginFields("firas", "hunter2", "https://github.com"));

            var syncA = new VaultSyncService(
                new CredentialStore(deviceADirectory), new CredentialTombstoneStore(deviceADirectory), new Sifra.Vault.Tags.TagStore(deviceADirectory),
                new Sifra.Vault.Attachments.AttachmentStore(deviceADirectory), new AttachmentTombstoneStore(deviceADirectory),
                new DropboxVaultEnvelopeCloudStore(apiClient, authA));
            var syncB = new VaultSyncService(
                new CredentialStore(deviceBDirectory), new CredentialTombstoneStore(deviceBDirectory), new Sifra.Vault.Tags.TagStore(deviceBDirectory),
                new Sifra.Vault.Attachments.AttachmentStore(deviceBDirectory), new AttachmentTombstoneStore(deviceBDirectory),
                new DropboxVaultEnvelopeCloudStore(apiClient, authB));

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
