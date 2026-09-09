using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Session;

namespace Sifra.Vault.Tests;

public sealed class VaultRecoveryServiceTests : IDisposable
{
    private const string InitialPassword = "correct-horse-battery-staple";
    private const string NewPassword = "new-correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public VaultRecoveryServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-recovery-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private VaultService CreateVaultService(AuditLogger? auditLogger = null) =>
        new(new VaultStore(_dataDirectory), auditLogger);

    private VaultAuthenticator CreateAuthenticator() =>
        new(new VaultAccessCredentialStore(_dataDirectory), new FileAccessAuditLog(Path.Combine(_dataDirectory, "vault-access.log")));

    private VaultEncryptionService CreateEncryption() => new(new VaultMasterKeyStore(_dataDirectory));

    private CredentialService CreateCredentialService() =>
        new(new CredentialStore(_dataDirectory), CreateEncryption(), new FakeCredentialClipboard());

    /// <summary>Full setup: create the vault, link the recovery key to the master key, seed a credential.</summary>
    private (VaultRecoveryService recovery, VaultAuthenticator authenticator, string recoveryKey, string credentialId) SetUpVaultWithOneCredential()
    {
        var vaultService = CreateVaultService();
        var authenticator = CreateAuthenticator();
        var recoveryKey = vaultService.CreateVault();

        var recovery = new VaultRecoveryService(vaultService, authenticator, CreateEncryption());
        recovery.EstablishRecoverySlot(recoveryKey, InitialPassword);

        var credentialId = CreateCredentialService().Add(InitialPassword, "GitHub", "firas", "hunter2", "https://github.com");

        return (recovery, authenticator, recoveryKey, credentialId);
    }

    [Fact]
    public void Recover_WithValidRecoveryKey_RestoresAccessToTheVaultIncludingExistingCredentials()
    {
        // Acceptance + REQ-009: recovery must restore real access, not
        // just reset a login check. Prove it by reading the credential
        // seeded BEFORE recovery, using the NEW password AFTER recovery.
        var (recovery, authenticator, recoveryKey, credentialId) = SetUpVaultWithOneCredential();

        recovery.Recover(recoveryKey, NewPassword);

        Assert.True(authenticator.Authenticate(NewPassword));
        Assert.False(authenticator.Authenticate(InitialPassword));

        var view = CreateCredentialService().GetById(NewPassword, credentialId);
        Assert.Equal("firas", view.Username);
        Assert.Equal("hunter2", view.Password);
    }

    [Fact]
    public void Recover_DoesNotTouchCredentialsJsonAtAll()
    {
        // Same proof as STORY-005, one level deeper: recovery re-wraps a
        // key, it never re-encrypts credential data.
        var (recovery, _, recoveryKey, _) = SetUpVaultWithOneCredential();
        var before = File.ReadAllText(Path.Combine(_dataDirectory, "credentials.json"));

        recovery.Recover(recoveryKey, NewPassword);

        var after = File.ReadAllText(Path.Combine(_dataDirectory, "credentials.json"));
        Assert.Equal(before, after);
    }

    [Fact]
    public void Recover_WithAnInvalidRecoveryKey_ThrowsAndDisplaysAnError()
    {
        // Failure path: "invalid recovery key."
        var (recovery, authenticator, _, _) = SetUpVaultWithOneCredential();

        Assert.Throws<InvalidRecoveryKeyException>(() => recovery.Recover("WRONG-KEY-VALUE", NewPassword));

        Assert.True(authenticator.Authenticate(InitialPassword)); // unchanged
    }

    [Fact]
    public void Recover_WithAnAlreadyUsedRecoveryKey_ThrowsExpired()
    {
        // Failure path: "recovery key expired" — modeled as single-use,
        // matching the original data model design ("consumed on recovery").
        var (recovery, _, recoveryKey, _) = SetUpVaultWithOneCredential();
        recovery.Recover(recoveryKey, NewPassword); // first use succeeds

        Assert.Throws<RecoveryKeyExpiredException>(() => recovery.Recover(recoveryKey, "yet-another-password"));
    }

    [Fact]
    public void Recover_WithATooShortNewPassword_ThrowsWithoutConsumingTheRecoveryKey()
    {
        var (recovery, _, recoveryKey, _) = SetUpVaultWithOneCredential();

        Assert.Throws<WeakMasterPasswordException>(() => recovery.Recover(recoveryKey, "short"));

        // The key must still be usable — a rejected attempt shouldn't burn the single use.
        recovery.Recover(recoveryKey, NewPassword);
        Assert.True(true); // no exception means the key was still valid
    }

    [Fact]
    public void Recover_WithAuditLoggerProvided_LogsTheAttemptWithoutLoggingTheRecoveryKeyOrPassword()
    {
        // Trust: recovery attempts are logged with user ID and timestamp.
        var vaultService = CreateVaultService();
        var authenticator = CreateAuthenticator();
        var recoveryKey = vaultService.CreateVault();
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var recovery = new VaultRecoveryService(vaultService, authenticator, CreateEncryption(), logger);
        recovery.EstablishRecoverySlot(recoveryKey, InitialPassword);

        recovery.Recover(recoveryKey, NewPassword);

        var lines = sink.ReadAll();
        var recoverLine = Assert.Single(lines, l => l.Contains("Recover"));
        Assert.Contains("outcome=success", recoverLine);
        Assert.DoesNotContain(recoveryKey, recoverLine);
        Assert.DoesNotContain(NewPassword, recoverLine);
    }

    [Fact]
    public void Recover_WhenTheRecoveryKeyIsInvalid_StillLogsTheAttempt()
    {
        var vaultService = CreateVaultService();
        var authenticator = CreateAuthenticator();
        var recoveryKey = vaultService.CreateVault();
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var recovery = new VaultRecoveryService(vaultService, authenticator, CreateEncryption(), logger);
        recovery.EstablishRecoverySlot(recoveryKey, InitialPassword);

        Assert.Throws<InvalidRecoveryKeyException>(() => recovery.Recover("WRONG-KEY", NewPassword));

        var lines = sink.ReadAll();
        Assert.Contains(lines, l => l.Contains("Recover") && l.Contains("outcome=invalid_key"));
    }

    [Fact]
    public void Recover_WhenLoggingServiceIsUnavailable_PropagatesAClearException()
    {
        // Failure path: "logging service is unavailable."
        var vaultService = CreateVaultService();
        var authenticator = CreateAuthenticator();
        var recoveryKey = vaultService.CreateVault();
        var alerts = new LocalFakeAdminAlertSink();
        var logger = new AuditLogger(new AlwaysFailingSink(), alerts, maxAttempts: 2, retryDelay: TimeSpan.Zero);
        var recovery = new VaultRecoveryService(vaultService, authenticator, CreateEncryption(), logger);
        recovery.EstablishRecoverySlot(recoveryKey, InitialPassword);

        Assert.Throws<AuditLoggingFailedException>(() => recovery.Recover(recoveryKey, NewPassword));

        Assert.Single(alerts.Alerts);
    }

    private sealed class AlwaysFailingSink : IAuditLogSink
    {
        public void Write(OperationLogEntry entry) => throw new AuditSinkUnavailableException("Simulated logging service outage.");
    }
}
