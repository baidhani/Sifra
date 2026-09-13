using System.IO;
using System.Net.Http;
using Sifra.Vault;
using Sifra.Vault.Attachments;
using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Health;
using Sifra.Vault.Session;
using Sifra.Vault.Tags;

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
    public CredentialAttachmentService Attachments { get; }
    public PasswordHistoryService PasswordHistory { get; }
    public CredentialIconService CredentialIcons { get; }
    public TagService Tags { get; }
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
        PasswordHistory = new PasswordHistoryService(
            new PasswordHistoryStore(null), new VaultEncryptionService(new VaultMasterKeyStore(null)));
        Credentials = new CredentialService(
            new CredentialStore(null), new VaultEncryptionService(new VaultMasterKeyStore(null)),
            new TextCopyCredentialClipboard(), auditLogger, PasswordHistory);
        Attachments = new CredentialAttachmentService(
            new AttachmentStore(null), new VaultEncryptionService(new VaultMasterKeyStore(null)), auditLogger);
        CredentialIcons = new CredentialIconService(
            Credentials, new CredentialIconImageStore(null), new GoogleFaviconFetcher(new HttpClient()));
        Tags = new TagService(new TagStore(null));
        PasswordHealth = new PasswordHealthService(Credentials, auditLogger);
    }
}
