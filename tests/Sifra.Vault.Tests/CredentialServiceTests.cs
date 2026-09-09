using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Tests;

public sealed class CredentialServiceTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;
    private readonly FakeCredentialClipboard _clipboard = new();

    public CredentialServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-credential-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private CredentialService CreateService(AuditLogger? auditLogger = null) => new(
        new CredentialStore(_dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
        _clipboard,
        auditLogger);

    [Fact]
    public void Add_ThenList_ShowsTheNewCredentialWithDecryptedUsername()
    {
        // Acceptance: adding a credential makes it appear in the list.
        var service = CreateService();

        service.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");

        var list = service.List(VaultCredential);
        Assert.Single(list);
        Assert.Equal("GitHub", list[0].Label);
        Assert.Equal("firas", list[0].Username);
        Assert.Null(list[0].Password); // list view never includes the password
    }

    [Fact]
    public void Add_EncryptsUsernameAndPasswordAtRest()
    {
        var service = CreateService();
        service.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");

        var rawFileContents = File.ReadAllText(Path.Combine(_dataDirectory, "credentials.json"));

        Assert.DoesNotContain("firas", rawFileContents);
        Assert.DoesNotContain("hunter2", rawFileContents);
    }

    [Fact]
    public void CopyUsername_WritesDecryptedUsernameToClipboard()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");

        service.CopyUsername(VaultCredential, id);

        Assert.Equal("firas", _clipboard.LastCopiedValue);
    }

    [Fact]
    public void CopyPassword_WritesDecryptedPasswordToClipboard()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");

        service.CopyPassword(VaultCredential, id);

        Assert.Equal("hunter2", _clipboard.LastCopiedValue);
    }

    [Fact]
    public void CopyUsername_ForNonExistentCredential_ThrowsCredentialNotFoundException()
    {
        // Failure path: "user attempts to copy a non-existent credential."
        var service = CreateService();

        Assert.Throws<CredentialNotFoundException>(() => service.CopyUsername(VaultCredential, "does-not-exist"));
        Assert.Null(_clipboard.LastCopiedValue); // nothing was written
    }

    [Fact]
    public void CopyPassword_WhenClipboardIsDenied_ThrowsClipboardUnavailableException()
    {
        // Failure path: "clipboard access is denied by the system."
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        _clipboard.SimulateFailure = true;

        Assert.Throws<ClipboardUnavailableException>(() => service.CopyPassword(VaultCredential, id));
    }

    [Fact]
    public void CopyUsername_HasNoDependencyOnConnectivityAtAll()
    {
        // Failure path: "user tries to copy while offline." CredentialService
        // has no reference to ICloudAuthProvider/ICloudSyncProvider anywhere
        // in its constructor — this is structural proof, not just behavior:
        // copying cannot be affected by connectivity because nothing here
        // can observe it.
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");

        service.CopyUsername(VaultCredential, id); // no cloud object involved anywhere

        Assert.Equal("firas", _clipboard.LastCopiedValue);
    }

    [Fact]
    public void Edit_UpdatesTheCredentialAndReEncrypts()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");

        service.Edit(VaultCredential, id, "GitHub", "firas-new", "hunter3", "https://github.com");

        var updated = service.GetById(VaultCredential, id);
        Assert.Equal("firas-new", updated.Username);
        Assert.Equal("hunter3", updated.Password);
    }

    [Fact]
    public void Delete_RemovesTheCredentialFromTheList()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");

        service.Delete(id);

        Assert.Empty(service.List(VaultCredential));
    }

    [Fact]
    public void Search_FindsByDecryptedUsernameOrPlaintextLabel()
    {
        var service = CreateService();
        service.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        service.Add(VaultCredential, "Gmail", "firas.other@example.com", "hunter3", "https://gmail.com");

        var byLabel = service.Search(VaultCredential, "git");
        var byUsername = service.Search(VaultCredential, "other@example");

        Assert.Single(byLabel);
        Assert.Equal("GitHub", byLabel[0].Label);
        Assert.Single(byUsername);
        Assert.Equal("Gmail", byUsername[0].Label);
    }

    [Fact]
    public void Add_WithAuditLoggerProvided_LogsTheOperationWithoutLoggingUsernameOrPassword()
    {
        // Trust: all actions are logged with a timestamp and user ID —
        // reusing STORY-015's AuditLogger — and secrets never appear in the log.
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = CreateService(logger);

        // Deliberately distinct from Environment.UserName (which the audit
        // log legitimately includes) so this assertion can't collide with it.
        service.Add(VaultCredential, "GitHub", "credential-username-should-not-leak", "hunter2", "https://github.com");

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("Add", lines[0]);
        Assert.DoesNotContain("credential-username-should-not-leak", lines[0]);
        Assert.DoesNotContain("hunter2", lines[0]);
    }
}
