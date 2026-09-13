using Sifra.Vault.Attachments;
using Sifra.Vault.Audit;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Tests;

public sealed class CredentialAttachmentServiceTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";
    private const string CredentialId = "cred-1";

    private readonly string _dataDirectory;

    public CredentialAttachmentServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-attachment-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private CredentialAttachmentService CreateService() => new(
        new AttachmentStore(_dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)));

    [Fact]
    public void Add_ThenGetDecryptedBytes_RoundTripsTheOriginalContent()
    {
        var service = CreateService();
        var content = new byte[] { 1, 2, 3, 4, 250, 251, 252 };

        var id = service.Add(VaultCredential, CredentialId, "photo.png", AttachmentKind.Image, content);
        var decrypted = service.GetDecryptedBytes(VaultCredential, id);

        Assert.Equal(content, decrypted);
    }

    [Fact]
    public void Add_ThenList_ShowsTheAttachmentMetadata()
    {
        var service = CreateService();
        service.Add(VaultCredential, CredentialId, "statement.pdf", AttachmentKind.File, new byte[] { 9, 9, 9 });

        var list = service.List(CredentialId);

        Assert.Single(list);
        Assert.Equal("statement.pdf", list[0].FileName);
        Assert.Equal(AttachmentKind.File, list[0].Kind);
        Assert.Equal(3, list[0].SizeBytes);
    }

    [Fact]
    public void List_OnlyReturnsAttachmentsForTheRequestedCredential()
    {
        var service = CreateService();
        service.Add(VaultCredential, "cred-a", "a.png", AttachmentKind.Image, new byte[] { 1 });
        service.Add(VaultCredential, "cred-b", "b.png", AttachmentKind.Image, new byte[] { 2 });

        Assert.Single(service.List("cred-a"));
        Assert.Single(service.List("cred-b"));
        Assert.Empty(service.List("cred-c"));
    }

    [Fact]
    public void RawBlobStorage_NeverContainsThePlaintextFileContent()
    {
        var service = CreateService();
        var content = System.Text.Encoding.UTF8.GetBytes("this-is-the-secret-file-content");

        service.Add(VaultCredential, CredentialId, "secret.txt", AttachmentKind.File, content);

        var blobFiles = Directory.GetFiles(Path.Combine(_dataDirectory, "attachments"));
        Assert.Single(blobFiles);
        var raw = File.ReadAllBytes(blobFiles[0]);
        var rawAsText = System.Text.Encoding.Latin1.GetString(raw);
        Assert.DoesNotContain("this-is-the-secret-file-content", rawAsText);
    }

    [Fact]
    public void GetDecryptedBytes_WithWrongVaultCredential_ThrowsRatherThanReturningGarbage()
    {
        // Failure path: wrong vault credential must not silently decrypt to corrupted bytes.
        var service = CreateService();
        var id = service.Add(VaultCredential, CredentialId, "photo.png", AttachmentKind.Image, new byte[] { 1, 2, 3 });

        Assert.Throws<VaultDecryptionFailedException>(() => service.GetDecryptedBytes("wrong-credential", id));
    }

    [Fact]
    public void Delete_RemovesBothMetadataAndTheBlobFile()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, CredentialId, "photo.png", AttachmentKind.Image, new byte[] { 1, 2, 3 });

        service.Delete(id);

        Assert.Empty(service.List(CredentialId));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dataDirectory, "attachments")));
    }

    [Fact]
    public void DeleteAllForCredential_RemovesEveryAttachmentForThatCredentialOnly()
    {
        var service = CreateService();
        service.Add(VaultCredential, "cred-a", "a1.png", AttachmentKind.Image, new byte[] { 1 });
        service.Add(VaultCredential, "cred-a", "a2.png", AttachmentKind.Image, new byte[] { 2 });
        service.Add(VaultCredential, "cred-b", "b.png", AttachmentKind.Image, new byte[] { 3 });

        service.DeleteAllForCredential("cred-a");

        Assert.Empty(service.List("cred-a"));
        Assert.Single(service.List("cred-b"));
    }

    [Fact]
    public void Add_WithAuditLoggerProvided_LogsMetadataWithoutFileContent()
    {
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = new CredentialAttachmentService(
            new AttachmentStore(_dataDirectory),
            new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
            logger);

        service.Add(VaultCredential, CredentialId, "photo.png", AttachmentKind.Image,
            System.Text.Encoding.UTF8.GetBytes("leaked-file-bytes-marker"));

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("Add", lines[0]);
        Assert.Contains("photo.png", lines[0]);
        Assert.DoesNotContain("leaked-file-bytes-marker", lines[0]);
    }
}
