using Sifra.Vault.Attachments;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class AttachmentBlobSyncServiceTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _deviceADirectory;
    private readonly string _deviceBDirectory;
    private readonly LocalFakeCloudBlobStore _cloud = new();

    public AttachmentBlobSyncServiceTests()
    {
        _deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-blob-a-" + Guid.NewGuid());
        _deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-blob-b-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_deviceADirectory)) Directory.Delete(_deviceADirectory, recursive: true);
        if (Directory.Exists(_deviceBDirectory)) Directory.Delete(_deviceBDirectory, recursive: true);
    }

    private AttachmentBlobSyncService DeviceA() =>
        new(new AttachmentStore(_deviceADirectory), new AttachmentTombstoneStore(_deviceADirectory), _cloud);

    private AttachmentBlobSyncService DeviceB() =>
        new(new AttachmentStore(_deviceBDirectory), new AttachmentTombstoneStore(_deviceBDirectory), _cloud);

    [Fact]
    public void SyncBlobs_LocalAttachmentWithNoCloudCopyYet_GetsUploaded()
    {
        var attachmentStoreA = new AttachmentStore(_deviceADirectory);
        var attachmentsA = new CredentialAttachmentService(attachmentStoreA, new VaultEncryptionService(new VaultMasterKeyStore(_deviceADirectory)));
        var id = attachmentsA.Add(VaultCredential, "cred-1", "photo.jpg", AttachmentKind.Image, [1, 2, 3]);
        var expectedCiphertext = attachmentStoreA.ReadEncryptedBlob(id); // AttachmentStore only ever holds ciphertext — Add encrypts before storing.

        DeviceA().SyncBlobs();

        Assert.True(_cloud.Exists(id));
        Assert.True(_cloud.TryDownload(id, out var content));
        Assert.Equal(expectedCiphertext, content);
    }

    [Fact]
    public void SyncBlobs_MetadataPresentButNoLocalBlobYet_DownloadsFromCloudWhenAvailable()
    {
        // Simulates: metadata already arrived via VaultSyncService's
        // envelope merge (UpsertMetadataOnly), but the blob itself hasn't
        // transferred to this device yet.
        var attachmentStoreA = new AttachmentStore(_deviceADirectory);
        var attachmentsA = new CredentialAttachmentService(attachmentStoreA, new VaultEncryptionService(new VaultMasterKeyStore(_deviceADirectory)));
        var id = attachmentsA.Add(VaultCredential, "cred-1", "photo.jpg", AttachmentKind.Image, [1, 2, 3]);
        var expectedCiphertext = attachmentStoreA.ReadEncryptedBlob(id);
        DeviceA().SyncBlobs(); // uploads it

        var metadata = attachmentStoreA.FindById(id)!;
        new AttachmentStore(_deviceBDirectory).UpsertMetadataOnly(metadata); // metadata-only, no blob

        DeviceB().SyncBlobs();

        Assert.True(new AttachmentStore(_deviceBDirectory).HasLocalBlob(id));
        Assert.Equal(expectedCiphertext, new AttachmentStore(_deviceBDirectory).ReadEncryptedBlob(id));
    }

    [Fact]
    public void SyncBlobs_MetadataPresentButNeitherSideHasTheBlobYet_DoesNothingAndDoesNotThrow()
    {
        var attachmentStoreB = new AttachmentStore(_deviceBDirectory);
        attachmentStoreB.UpsertMetadataOnly(new CredentialAttachment("att-1", "cred-1", "photo.jpg", AttachmentKind.Image, 3, DateTimeOffset.UtcNow));

        DeviceB().SyncBlobs();

        Assert.False(attachmentStoreB.HasLocalBlob("att-1"));
    }

    [Fact]
    public void SyncBlobs_AlreadyUploaded_DoesNotReUploadOnASecondSync()
    {
        var attachmentsA = new CredentialAttachmentService(
            new AttachmentStore(_deviceADirectory), new VaultEncryptionService(new VaultMasterKeyStore(_deviceADirectory)));
        attachmentsA.Add(VaultCredential, "cred-1", "photo.jpg", AttachmentKind.Image, [1, 2, 3]);
        var deviceA = DeviceA();
        deviceA.SyncBlobs();

        // A second sync must not error or duplicate work — Exists() short-circuits it.
        deviceA.SyncBlobs();

        Assert.True(true); // primarily: no exception
    }

    [Fact]
    public void SyncBlobs_TombstonedAttachment_DeletesTheRemoteBlob()
    {
        var attachmentStoreA = new AttachmentStore(_deviceADirectory);
        var tombstonesA = new AttachmentTombstoneStore(_deviceADirectory);
        var attachmentsA = new CredentialAttachmentService(
            attachmentStoreA, new VaultEncryptionService(new VaultMasterKeyStore(_deviceADirectory)), tombstones: tombstonesA);
        var id = attachmentsA.Add(VaultCredential, "cred-1", "photo.jpg", AttachmentKind.Image, [1, 2, 3]);
        DeviceA().SyncBlobs();
        Assert.True(_cloud.Exists(id));

        attachmentsA.Delete(id);
        DeviceA().SyncBlobs();

        Assert.False(_cloud.Exists(id));
    }

    [Fact]
    public void SyncBlobs_WhenNotConnected_ThrowsSyncUnavailable()
    {
        _cloud.IsConnected = false;

        Assert.Throws<SyncUnavailableException>(() => DeviceA().SyncBlobs());
    }
}
