using Sifra.Vault.Attachments;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class GoogleDriveCloudBlobStoreTests
{
    private static (GoogleDriveCloudBlobStore Store, FakeGoogleDriveBlobApiClient ApiClient) CreateConnectedStore()
    {
        var apiClient = new FakeGoogleDriveBlobApiClient();
        var auth = new LocalFakeCloudAuthProvider();
        auth.SignIn("someone@example.com");
        return (new GoogleDriveCloudBlobStore(apiClient, auth), apiClient);
    }

    [Fact]
    public void Exists_ForABlobNeverUploaded_ReturnsFalse()
    {
        var (store, _) = CreateConnectedStore();

        Assert.False(store.Exists("att-1"));
    }

    [Fact]
    public void Upload_ThenExistsAndDownload_RoundTripsTheSameContent()
    {
        var (store, _) = CreateConnectedStore();

        store.Upload("att-1", [1, 2, 3]);

        Assert.True(store.Exists("att-1"));
        Assert.True(store.TryDownload("att-1", out var content));
        Assert.Equal(new byte[] { 1, 2, 3 }, content);
    }

    [Fact]
    public void Delete_RemovesTheBlob()
    {
        var (store, _) = CreateConnectedStore();
        store.Upload("att-1", [1, 2, 3]);

        store.Delete("att-1");

        Assert.False(store.Exists("att-1"));
    }

    [Fact]
    public void Delete_ForABlobThatNeverExisted_DoesNotThrow()
    {
        var (store, _) = CreateConnectedStore();

        store.Delete("never-existed"); // best-effort — must not throw
        Assert.True(true);
    }

    [Fact]
    public void Exists_WhenNotConnected_ThrowsSyncUnavailable()
    {
        var apiClient = new FakeGoogleDriveBlobApiClient();
        var auth = new LocalFakeCloudAuthProvider(); // never signed in
        var store = new GoogleDriveCloudBlobStore(apiClient, auth);

        Assert.Throws<SyncUnavailableException>(() => store.Exists("att-1"));
    }

    [Fact]
    public void Upload_WhenTheApiClientFailsForNetworkReasons_PropagatesTheGoogleDriveException()
    {
        var (store, apiClient) = CreateConnectedStore();
        apiClient.SimulateNetworkFailure = true;

        Assert.Throws<GoogleDriveNetworkException>(() => store.Upload("att-1", [1, 2, 3]));
    }

    [Fact]
    public void AttachmentBlobSyncService_EndToEnd_TwoDevicesSharingOneFakeGoogleDriveApiClient_TransfersBlobs()
    {
        var apiClient = new FakeGoogleDriveBlobApiClient();
        var authA = new LocalFakeCloudAuthProvider();
        authA.SignIn("someone@example.com");
        var authB = new LocalFakeCloudAuthProvider();
        authB.SignIn("someone@example.com");

        var deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-gdrive-blob-a-" + Guid.NewGuid());
        var deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-gdrive-blob-b-" + Guid.NewGuid());
        try
        {
            var attachmentStoreA = new AttachmentStore(deviceADirectory);
            var attachmentsA = new CredentialAttachmentService(attachmentStoreA, new VaultEncryptionService(new VaultMasterKeyStore(deviceADirectory)));
            var id = attachmentsA.Add("correct-horse-battery-staple", "cred-1", "photo.jpg", AttachmentKind.Image, [1, 2, 3]);
            var expectedCiphertext = attachmentStoreA.ReadEncryptedBlob(id);

            var syncA = new AttachmentBlobSyncService(attachmentStoreA, new AttachmentTombstoneStore(deviceADirectory), new GoogleDriveCloudBlobStore(apiClient, authA));
            syncA.SyncBlobs();

            var attachmentStoreB = new AttachmentStore(deviceBDirectory);
            attachmentStoreB.UpsertMetadataOnly(attachmentStoreA.FindById(id)!);
            var syncB = new AttachmentBlobSyncService(attachmentStoreB, new AttachmentTombstoneStore(deviceBDirectory), new GoogleDriveCloudBlobStore(apiClient, authB));
            syncB.SyncBlobs();

            Assert.True(attachmentStoreB.HasLocalBlob(id));
            Assert.Equal(expectedCiphertext, attachmentStoreB.ReadEncryptedBlob(id));
        }
        finally
        {
            if (Directory.Exists(deviceADirectory)) Directory.Delete(deviceADirectory, recursive: true);
            if (Directory.Exists(deviceBDirectory)) Directory.Delete(deviceBDirectory, recursive: true);
        }
    }
}
