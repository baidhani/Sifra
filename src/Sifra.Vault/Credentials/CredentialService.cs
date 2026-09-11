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

    /// <summary>Decrypted list view. Password and every other secret field are never included here — only plaintext metadata.</summary>
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
                c.UpdatedAtUtc,
                Phone: c.Phone,
                IsFavorite: c.IsFavorite,
                Labels: c.Labels ?? Array.Empty<string>()))
            .ToList();
    }

    /// <summary>Full decrypted view including every secret field, for viewing/editing a single credential.</summary>
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
            record.UpdatedAtUtc,
            Phone: record.Phone,
            Notes: record.EncryptedNotesBase64 is null ? null : _encryption.Decrypt(record.EncryptedNotesBase64, key),
            AccountNumber: record.EncryptedAccountNumberBase64 is null ? null : _encryption.Decrypt(record.EncryptedAccountNumberBase64, key),
            Pin: record.EncryptedPinBase64 is null ? null : _encryption.Decrypt(record.EncryptedPinBase64, key),
            CustomFields: record.CustomFields?.Select(f => new CustomFieldView(f.Name, _encryption.Decrypt(f.EncryptedValueBase64, key), f.Type)).ToList()
                ?? new List<CustomFieldView>(),
            IsFavorite: record.IsFavorite,
            Labels: record.Labels ?? Array.Empty<string>());
    }

    public string Add(
        string vaultCredential, string label, string username, string password, string? url,
        string? phone = null, string? notes = null, string? accountNumber = null, string? pin = null,
        IReadOnlyList<(string Name, string Value, CustomFieldType Type)>? customFields = null,
        bool isFavorite = false, IReadOnlyList<string>? labels = null)
    {
        var key = _encryption.DeriveKey(vaultCredential);
        var id = Guid.NewGuid().ToString("N");

        _store.Upsert(new Credential(
            id,
            label,
            _encryption.Encrypt(username, key),
            _encryption.Encrypt(password, key),
            url,
            DateTimeOffset.UtcNow,
            Phone: phone,
            EncryptedNotesBase64: notes is null ? null : _encryption.Encrypt(notes, key),
            EncryptedAccountNumberBase64: accountNumber is null ? null : _encryption.Encrypt(accountNumber, key),
            EncryptedPinBase64: pin is null ? null : _encryption.Encrypt(pin, key),
            CustomFields: EncryptCustomFields(customFields, key),
            IsFavorite: isFavorite,
            Labels: labels));

        _auditLogger?.Log(nameof(Add), Environment.UserName, details: $"id={id}");
        return id;
    }

    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void Edit(
        string vaultCredential, string id, string label, string username, string password, string? url,
        string? phone = null, string? notes = null, string? accountNumber = null, string? pin = null,
        IReadOnlyList<(string Name, string Value, CustomFieldType Type)>? customFields = null,
        bool isFavorite = false, IReadOnlyList<string>? labels = null)
    {
        FindOrThrow(id);
        var key = _encryption.DeriveKey(vaultCredential);

        _store.Upsert(new Credential(
            id,
            label,
            _encryption.Encrypt(username, key),
            _encryption.Encrypt(password, key),
            url,
            DateTimeOffset.UtcNow,
            Phone: phone,
            EncryptedNotesBase64: notes is null ? null : _encryption.Encrypt(notes, key),
            EncryptedAccountNumberBase64: accountNumber is null ? null : _encryption.Encrypt(accountNumber, key),
            EncryptedPinBase64: pin is null ? null : _encryption.Encrypt(pin, key),
            CustomFields: EncryptCustomFields(customFields, key),
            IsFavorite: isFavorite,
            Labels: labels));

        _auditLogger?.Log(nameof(Edit), Environment.UserName, details: $"id={id}");
    }

    /// <summary>Toggles IsFavorite without needing every other field — a common, low-risk single-flag update.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void SetFavorite(string id, bool isFavorite)
    {
        var record = FindOrThrow(id);
        _store.Upsert(record with { IsFavorite = isFavorite, UpdatedAtUtc = DateTimeOffset.UtcNow });
        _auditLogger?.Log(nameof(SetFavorite), Environment.UserName, details: $"id={id} isFavorite={isFavorite}");
    }

    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void Delete(string id)
    {
        FindOrThrow(id);
        _store.Delete(id);
        _auditLogger?.Log(nameof(Delete), Environment.UserName, details: $"id={id}");
    }

    /// <summary>Matches against the decrypted username or the plaintext label/url/phone/labels — never logs the query text itself.</summary>
    public IReadOnlyList<CredentialView> Search(string vaultCredential, string query)
    {
        var results = List(vaultCredential)
            .Where(v =>
                v.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                v.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (v.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (v.Phone?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (v.Labels?.Any(l => l.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false))
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

    private List<CustomField>? EncryptCustomFields(IReadOnlyList<(string Name, string Value, CustomFieldType Type)>? customFields, byte[] key) =>
        customFields?.Select(f => new CustomField(f.Name, _encryption.Encrypt(f.Value, key), f.Type)).ToList();

    private Credential FindOrThrow(string id) =>
        _store.GetAll().FirstOrDefault(c => c.Id == id) ?? throw new CredentialNotFoundException(id);
}
