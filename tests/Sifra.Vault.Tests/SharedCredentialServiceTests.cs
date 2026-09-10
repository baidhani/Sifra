using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sharing;

namespace Sifra.Vault.Tests;

public sealed class SharedCredentialServiceTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";
    private const string RecipientPassphrase = "recipient-chosen-passphrase";

    private readonly string _dataDirectory;

    public SharedCredentialServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-sharing-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private CredentialService CreateCredentialService() => new(
        new CredentialStore(_dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
        new FakeCredentialClipboard());

    private SharedCredentialService CreateService(CredentialService credentials, AuditLogger? auditLogger = null) =>
        new(credentials, new ShareRegistryStore(_dataDirectory), auditLogger);

    [Fact]
    public void CreateShare_ThenAccept_TheRecipientReceivesAnEncryptedCopy()
    {
        // Acceptance: when shared, the recipient receives an encrypted copy.
        var credentials = CreateCredentialService();
        var credentialId = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var service = CreateService(credentials);

        var shareId = service.CreateShare(VaultCredential, credentialId, "alice", RecipientPassphrase);

        // The registry only ever holds ciphertext for this credential's secrets.
        var raw = File.ReadAllText(Path.Combine(_dataDirectory, "share-registry.json"));
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("firas", raw);

        var view = service.AcceptShare(shareId, RecipientPassphrase);
        Assert.Equal("GitHub", view.Label);
        Assert.Equal("firas", view.Username);
        Assert.Equal("hunter2", view.Password);
    }

    [Fact]
    public void AcceptShare_WithTheWrongPassphrase_ThrowsRatherThanReturningGarbage()
    {
        // Failure path: "sharing fails to encrypt credentials" (here, the
        // symmetric failure — a wrong key must not silently decrypt).
        var credentials = CreateCredentialService();
        var credentialId = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var service = CreateService(credentials);
        var shareId = service.CreateShare(VaultCredential, credentialId, "alice", RecipientPassphrase);

        Assert.Throws<VaultDecryptionFailedException>(() => service.AcceptShare(shareId, "wrong-passphrase"));
    }

    [Fact]
    public void CreateShare_ForANonExistentCredential_ThrowsInsteadOfSharingNothing()
    {
        var credentials = CreateCredentialService();
        var service = CreateService(credentials);

        Assert.Throws<CredentialNotFoundException>(
            () => service.CreateShare(VaultCredential, "does-not-exist", "alice", RecipientPassphrase));
    }

    [Fact]
    public void RevokeShare_ThenAccept_TheRecipientLosesAccess()
    {
        // Acceptance: when revoked, the recipient loses access.
        var credentials = CreateCredentialService();
        var credentialId = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var service = CreateService(credentials);
        var shareId = service.CreateShare(VaultCredential, credentialId, "alice", RecipientPassphrase);

        service.RevokeShare(shareId);

        Assert.Throws<ShareRevokedException>(() => service.AcceptShare(shareId, RecipientPassphrase));
    }

    [Fact]
    public void RevokeShare_CalledTwice_IsIdempotent()
    {
        var credentials = CreateCredentialService();
        var credentialId = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var service = CreateService(credentials);
        var shareId = service.CreateShare(VaultCredential, credentialId, "alice", RecipientPassphrase);

        service.RevokeShare(shareId);
        service.RevokeShare(shareId); // must not throw or change state further

        Assert.Throws<ShareRevokedException>(() => service.AcceptShare(shareId, RecipientPassphrase));
    }

    [Fact]
    public void RevokeShare_ForANonExistentShare_ThrowsShareNotFound()
    {
        var credentials = CreateCredentialService();
        var service = CreateService(credentials);

        Assert.Throws<ShareNotFoundException>(() => service.RevokeShare("does-not-exist"));
    }

    [Fact]
    public void AcceptShare_ForANonExistentShare_ThrowsShareNotFound()
    {
        var credentials = CreateCredentialService();
        var service = CreateService(credentials);

        Assert.Throws<ShareNotFoundException>(() => service.AcceptShare("does-not-exist", RecipientPassphrase));
    }

    [Fact]
    public void SharingActions_AreLoggedWithoutExposingThePassphraseOrTheCredentialSecret()
    {
        // Trust: sharing actions are logged.
        var credentials = CreateCredentialService();
        var credentialId = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = CreateService(credentials, logger);

        var shareId = service.CreateShare(VaultCredential, credentialId, "alice", RecipientPassphrase);
        service.AcceptShare(shareId, RecipientPassphrase);
        service.RevokeShare(shareId);

        var lines = sink.ReadAll();
        Assert.Contains(lines, l => l.Contains("CreateShare") && l.Contains("recipient=alice"));
        Assert.Contains(lines, l => l.Contains("AcceptShare") && l.Contains("outcome=accepted"));
        Assert.Contains(lines, l => l.Contains("RevokeShare"));
        Assert.All(lines, l => Assert.DoesNotContain("hunter2", l));
        Assert.All(lines, l => Assert.DoesNotContain(RecipientPassphrase, l));
    }

    [Fact]
    public void AcceptShare_AfterRevocation_StillLogsTheDeniedAttempt()
    {
        var credentials = CreateCredentialService();
        var credentialId = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = CreateService(credentials, logger);
        var shareId = service.CreateShare(VaultCredential, credentialId, "alice", RecipientPassphrase);
        service.RevokeShare(shareId);

        Assert.Throws<ShareRevokedException>(() => service.AcceptShare(shareId, RecipientPassphrase));

        var lines = sink.ReadAll();
        Assert.Contains(lines, l => l.Contains("AcceptShare") && l.Contains("outcome=revoked"));
    }
}
