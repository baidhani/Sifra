using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Session;

namespace Sifra.Vault.Tests;

public sealed class MasterPasswordServiceTests : IDisposable
{
    private const string OldPassword = "correct-horse-battery-staple";
    private const string NewPassword = "new-correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public MasterPasswordServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-master-password-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private VaultAuthenticator CreateAuthenticator() =>
        new(new VaultAccessCredentialStore(_dataDirectory), new FileAccessAuditLog(Path.Combine(_dataDirectory, "vault-access.log")));

    private VaultEncryptionService CreateEncryption() => new(new VaultMasterKeyStore(_dataDirectory));

    private CredentialService CreateCredentialService() =>
        new(new CredentialStore(_dataDirectory), CreateEncryption(), new FakeCredentialClipboard());

    [Fact]
    public void ChangePassword_WithCorrectCurrentPassword_UpdatesSuccessfully()
    {
        // Acceptance: changing the master password succeeds.
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(OldPassword);
        var service = new MasterPasswordService(authenticator, CreateEncryption());

        service.ChangePassword(OldPassword, NewPassword);

        Assert.False(authenticator.Authenticate(OldPassword));
        Assert.True(authenticator.Authenticate(NewPassword));
    }

    [Fact]
    public void ChangePassword_DoesNotReEncryptExistingCredentials_TheyRemainDecryptableUnderTheNewPassword()
    {
        // REQ-008, the whole point of this story: change the password
        // WITHOUT re-encrypting all secrets. Prove it by seeding a
        // credential under the old password, changing the password, then
        // reading the SAME ciphertext back successfully with the new one —
        // nothing about the stored credential needed to be touched.
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(OldPassword);
        var credentialService = CreateCredentialService();
        var id = credentialService.Add(OldPassword, "GitHub", "firas", "hunter2", "https://github.com");
        var ciphertextBefore = File.ReadAllText(Path.Combine(_dataDirectory, "credentials.json"));

        var masterPasswordService = new MasterPasswordService(authenticator, CreateEncryption());
        masterPasswordService.ChangePassword(OldPassword, NewPassword);

        var ciphertextAfter = File.ReadAllText(Path.Combine(_dataDirectory, "credentials.json"));
        Assert.Equal(ciphertextBefore, ciphertextAfter); // credentials.json was never rewritten

        var view = credentialService.GetById(NewPassword, id);
        Assert.Equal("firas", view.Username);
        Assert.Equal("hunter2", view.Password);
    }

    [Fact]
    public void ChangePassword_WithIncorrectCurrentPassword_ThrowsAndLeavesPasswordUnchanged()
    {
        // Failure path: "incorrect current password."
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(OldPassword);
        var service = new MasterPasswordService(authenticator, CreateEncryption());

        Assert.Throws<IncorrectMasterPasswordException>(() => service.ChangePassword("wrong-guess", NewPassword));

        Assert.True(authenticator.Authenticate(OldPassword)); // unchanged
    }

    [Fact]
    public void ChangePassword_WithATooShortNewPassword_ThrowsAndLeavesPasswordUnchanged()
    {
        // Failure path: "new password does not meet security criteria."
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(OldPassword);
        var service = new MasterPasswordService(authenticator, CreateEncryption());

        Assert.Throws<WeakMasterPasswordException>(() => service.ChangePassword(OldPassword, "short"));

        Assert.True(authenticator.Authenticate(OldPassword)); // unchanged
    }

    [Fact]
    public void ChangePassword_WithAuditLoggerProvided_LogsTheEventWithoutLoggingEitherPassword()
    {
        // Trust: password change events are logged with user ID and timestamp.
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(OldPassword);
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = new MasterPasswordService(authenticator, CreateEncryption(), logger);

        service.ChangePassword(OldPassword, NewPassword);

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("ChangePassword", lines[0]);
        Assert.DoesNotContain(OldPassword, lines[0]);
        Assert.DoesNotContain(NewPassword, lines[0]);
    }

    [Fact]
    public void ChangePassword_WhenLoggingServiceIsUnavailable_PropagatesAClearExceptionButHasAlreadyChangedThePassword()
    {
        // Failure path: "logging service is unavailable." Consistent with
        // every prior story: the primary operation (here, the password
        // change itself) already completed before the log call, which is
        // last. The logging failure is surfaced loudly (retried, admin
        // alerted, clear exception) rather than silently swallowed — but
        // it does not undo the already-successful password change.
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(OldPassword);
        var alerts = new LocalFakeAdminAlertSink();
        var logger = new AuditLogger(new AlwaysFailingSink(), alerts, maxAttempts: 2, retryDelay: TimeSpan.Zero);
        var service = new MasterPasswordService(authenticator, CreateEncryption(), logger);

        Assert.Throws<AuditLoggingFailedException>(() => service.ChangePassword(OldPassword, NewPassword));

        Assert.True(authenticator.Authenticate(NewPassword)); // the change itself still succeeded
        Assert.Single(alerts.Alerts);
    }

    private sealed class AlwaysFailingSink : IAuditLogSink
    {
        public void Write(OperationLogEntry entry) => throw new AuditSinkUnavailableException("Simulated logging service outage.");
    }
}
