using System.IO;
using System.Net.Http;
using Sifra.Vault;
using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Health;
using Sifra.Vault.Session;

namespace Sifra.Desktop;

/// <summary>
/// Wires up the same Sifra.Vault services the CLI uses, pointed at the
/// real default data directory (%AppData%\Sifra) — this app is a UI on
/// top of the existing vault, not a second implementation of it.
/// </summary>
public sealed class AppServices
{
    public VaultStore VaultStore { get; }
    public VaultService VaultService { get; }
    public VaultRecoveryService VaultRecovery { get; }
    public VaultAuthenticator VaultAuth { get; }
    public CredentialService Credentials { get; }
    public PasswordHealthService PasswordHealth { get; }
    public IBreachChecker BreachChecker { get; } = new HibpBreachChecker(new HttpClient());

    public AppServices()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra");

        var auditLogger = new AuditLogger(
            new FileAuditLogSink(Path.Combine(dataDirectory, "operations.log")),
            new LocalFakeAdminAlertSink());

        VaultStore = new VaultStore(null);
        VaultService = new VaultService(VaultStore, auditLogger);
        VaultAuth = new VaultAuthenticator(
            new VaultAccessCredentialStore(null),
            new FileAccessAuditLog(Path.Combine(dataDirectory, "vault-access.log")),
            auditLogger);
        VaultRecovery = new VaultRecoveryService(
            VaultService, VaultAuth, new VaultEncryptionService(new VaultMasterKeyStore(null)), auditLogger);
        Credentials = new CredentialService(
            new CredentialStore(null), new VaultEncryptionService(new VaultMasterKeyStore(null)),
            new TextCopyCredentialClipboard(), auditLogger);
        PasswordHealth = new PasswordHealthService(Credentials, auditLogger);
    }
}
