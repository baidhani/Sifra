using Sifra.Vault.Audit;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Credentials;

/// <summary>
/// Add/view/edit/delete/search credentials, and copy username/password to
/// the clipboard. Every method that touches a secret field takes the vault
/// credential explicitly and derives the encryption key from it — there is
/// no persistent in-memory session/unlock state here by design; that is
/// STORY-004/005's job (lock/unlock), which this can plug into later
/// without a rewrite. No secret value is ever passed to the audit logger.
/// </summary>
public sealed class CredentialService
{
    private readonly CredentialStore _store;
    private readonly VaultEncryptionService _encryption;
    private readonly ICredentialClipboard _clipboard;
    private readonly AuditLogger? _auditLogger;

    public CredentialService(
        CredentialStore store,
        VaultEncryptionService encryption,
        ICredentialClipboard clipboard,
        AuditLogger? auditLogger = null)
    {
        _store = store;
        _encryption = encryption;
        _clipboard = clipboard;
        _auditLogger = auditLogger;
    }

    /// <summary>Decrypted list view. Password is never included here.</summary>
    public IReadOnlyList<CredentialView> List(string vaultCredential)
    {
        var key = _encryption.DeriveKey(vaultCredential);
        return _store.GetAll()
            .Select(c => new CredentialView(
                c.Id,
                c.Label,
                _encryption.Decrypt(c.EncryptedUsernameBase64, key),
                Password: null,
                c.Url,
                c.UpdatedAtUtc))
            .ToList();
    }

    /// <summary>Full decrypted view including the password, for viewing/editing a single credential.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public CredentialView GetById(string vaultCredential, string id)
    {
        var record = FindOrThrow(id);
        var key = _encryption.DeriveKey(vaultCredential);
        return new CredentialView(
            record.Id,
            record.Label,
            _encryption.Decrypt(record.EncryptedUsernameBase64, key),
            _encryption.Decrypt(record.EncryptedPasswordBase64, key),
            record.Url,
            record.UpdatedAtUtc);
    }

    public string Add(string vaultCredential, string label, string username, string password, string? url)
    {
        var key = _encryption.DeriveKey(vaultCredential);
        var id = Guid.NewGuid().ToString("N");

        _store.Upsert(new Credential(
            id,
            label,
            _encryption.Encrypt(username, key),
            _encryption.Encrypt(password, key),
            url,
            DateTimeOffset.UtcNow));

        _auditLogger?.Log(nameof(Add), Environment.UserName, details: $"id={id}");
        return id;
    }

    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void Edit(string vaultCredential, string id, string label, string username, string password, string? url)
    {
        FindOrThrow(id);
        var key = _encryption.DeriveKey(vaultCredential);

        _store.Upsert(new Credential(
            id,
            label,
            _encryption.Encrypt(username, key),
            _encryption.Encrypt(password, key),
            url,
            DateTimeOffset.UtcNow));

        _auditLogger?.Log(nameof(Edit), Environment.UserName, details: $"id={id}");
    }

    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void Delete(string id)
    {
        FindOrThrow(id);
        _store.Delete(id);
        _auditLogger?.Log(nameof(Delete), Environment.UserName, details: $"id={id}");
    }

    /// <summary>Matches against the decrypted username or the plaintext label/url — never logs the query text itself.</summary>
    public IReadOnlyList<CredentialView> Search(string vaultCredential, string query)
    {
        var results = List(vaultCredential)
            .Where(v =>
                v.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                v.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (v.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        _auditLogger?.Log(nameof(Search), Environment.UserName, details: $"resultCount={results.Count}");
        return results;
    }

    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    /// <exception cref="ClipboardUnavailableException">The system denied clipboard access.</exception>
    public void CopyUsername(string vaultCredential, string id)
    {
        var view = GetById(vaultCredential, id);
        _clipboard.SetText(view.Username);
        _auditLogger?.Log(nameof(CopyUsername), Environment.UserName, details: $"id={id}");
    }

    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    /// <exception cref="ClipboardUnavailableException">The system denied clipboard access.</exception>
    public void CopyPassword(string vaultCredential, string id)
    {
        var view = GetById(vaultCredential, id);
        _clipboard.SetText(view.Password!);
        _auditLogger?.Log(nameof(CopyPassword), Environment.UserName, details: $"id={id}");
    }

    private Credential FindOrThrow(string id) =>
        _store.GetAll().FirstOrDefault(c => c.Id == id) ?? throw new CredentialNotFoundException(id);
}
