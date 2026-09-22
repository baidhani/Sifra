using Sifra.Vault.Attachments;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Dropbox;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class VaultProviderMigrationServiceTests
{
    private static VaultSyncEnvelope EnvelopeWith(params Credential[] credentials) => new(credentials, [], [], [], []);

    [Fact]
    public void Migrate_SourceHasAVault_CopiesItToTheDestination()
    {
        var source = new LocalFakeVaultEnvelopeCloudStore();
        var destination = new LocalFakeVaultEnvelopeCloudStore();
        var credential = new Credential("cred-1", "GitHub", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        source.TryPush(EnvelopeWith(credential), null, out _);

        var result = new VaultProviderMigrationService().Migrate(source, new LocalFakeCloudBlobStore(), destination, new LocalFakeCloudBlobStore());

        Assert.Equal(VaultMigrationOutcome.Migrated, result.Outcome);
        var pulled = destination.Pull();
        Assert.NotNull(pulled);
        Assert.Single(pulled!.Value.Envelope.Credentials);
        Assert.Equal("cred-1", pulled.Value.Envelope.Credentials[0].Id);
    }

    [Fact]
    public void Migrate_SourceHasNoVaultYet_ReturnsNothingToMigrate()
    {
        var source = new LocalFakeVaultEnvelopeCloudStore();
        var destination = new LocalFakeVaultEnvelopeCloudStore();

        var result = new VaultProviderMigrationService().Migrate(source, new LocalFakeCloudBlobStore(), destination, new LocalFakeCloudBlobStore());

        Assert.Equal(VaultMigrationOutcome.NothingToMigrate, result.Outcome);
        Assert.Null(destination.Pull());
    }

    [Fact]
    public void Migrate_DestinationAlreadyHasData_RefusesRatherThanOverwriting()
    {
        var source = new LocalFakeVaultEnvelopeCloudStore();
        var destination = new LocalFakeVaultEnvelopeCloudStore();
        source.TryPush(EnvelopeWith(new Credential("cred-1", "GitHub", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)), null, out _);
        destination.TryPush(EnvelopeWith(new Credential("cred-2", "Gmail", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)), null, out _);

        var result = new VaultProviderMigrationService().Migrate(source, new LocalFakeCloudBlobStore(), destination, new LocalFakeCloudBlobStore());

        Assert.Equal(VaultMigrationOutcome.DestinationAlreadyHasData, result.Outcome);
        // The destination's own pre-existing data must survive untouched.
        Assert.Equal("cred-2", destination.Pull()!.Value.Envelope.Credentials[0].Id);
    }

    [Fact]
    public void Migrate_TransfersAttachmentBlobsThatExistOnTheSource()
    {
        var source = new LocalFakeVaultEnvelopeCloudStore();
        var destination = new LocalFakeVaultEnvelopeCloudStore();
        var sourceBlobs = new LocalFakeCloudBlobStore();
        var destinationBlobs = new LocalFakeCloudBlobStore();
        sourceBlobs.Upload("att-1", [1, 2, 3]);
        var attachment = new CredentialAttachment("att-1", "cred-1", "photo.jpg", AttachmentKind.Image, 3, DateTimeOffset.UtcNow);
        source.TryPush(new VaultSyncEnvelope([], [], [], [attachment], []), null, out _);

        var result = new VaultProviderMigrationService().Migrate(source, sourceBlobs, destination, destinationBlobs);

        Assert.Equal(1, result.AttachmentsTransferred);
        Assert.True(destinationBlobs.TryDownload("att-1", out var content));
        Assert.Equal(new byte[] { 1, 2, 3 }, content);
    }

    [Fact]
    public void Migrate_AttachmentMetadataWithNoLocalBlobOnTheSource_SkipsItWithoutFailing()
    {
        var source = new LocalFakeVaultEnvelopeCloudStore();
        var destination = new LocalFakeVaultEnvelopeCloudStore();
        var attachment = new CredentialAttachment("att-1", "cred-1", "photo.jpg", AttachmentKind.Image, 3, DateTimeOffset.UtcNow);
        source.TryPush(new VaultSyncEnvelope([], [], [], [attachment], []), null, out _); // metadata only — its blob was never uploaded to source either

        var result = new VaultProviderMigrationService().Migrate(source, new LocalFakeCloudBlobStore(), destination, new LocalFakeCloudBlobStore());

        Assert.Equal(VaultMigrationOutcome.Migrated, result.Outcome);
        Assert.Equal(0, result.AttachmentsTransferred);
    }

    [Fact]
    public void Migrate_RealCrossProviderScenario_DropboxToGoogleDrive_WorksEndToEnd()
    {
        // The actual real-world case: migrating between two genuinely
        // different provider implementations, not two instances of the
        // same fake — proves the service is truly provider-agnostic.
        var dropboxApiClient = new FakeDropboxEnvelopeApiClient();
        var dropboxAuth = new LocalFakeCloudAuthProvider();
        dropboxAuth.SignIn("someone@example.com");
        var dropboxStore = new DropboxVaultEnvelopeCloudStore(dropboxApiClient, dropboxAuth);

        var driveApiClient = new FakeGoogleDriveEnvelopeApiClient();
        var driveAuth = new LocalFakeCloudAuthProvider();
        driveAuth.SignIn("someone@example.com");
        var driveStore = new GoogleDriveVaultEnvelopeCloudStore(driveApiClient, driveAuth);

        var credential = new Credential("cred-1", "GitHub", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        dropboxStore.TryPush(EnvelopeWith(credential), null, out _);

        var result = new VaultProviderMigrationService().Migrate(
            dropboxStore, new LocalFakeCloudBlobStore(), driveStore, new LocalFakeCloudBlobStore());

        Assert.Equal(VaultMigrationOutcome.Migrated, result.Outcome);
        var pulledFromDrive = driveStore.Pull();
        Assert.NotNull(pulledFromDrive);
        Assert.Equal("cred-1", pulledFromDrive!.Value.Envelope.Credentials[0].Id);
    }

    [Fact]
    public void Migrate_WhenSourceIsNotConnected_ThrowsSyncUnavailable()
    {
        var source = new LocalFakeVaultEnvelopeCloudStore { IsConnected = false };
        var destination = new LocalFakeVaultEnvelopeCloudStore();

        Assert.Throws<SyncUnavailableException>(() =>
            new VaultProviderMigrationService().Migrate(source, new LocalFakeCloudBlobStore(), destination, new LocalFakeCloudBlobStore()));
    }
}
