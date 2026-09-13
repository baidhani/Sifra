using Sifra.Vault.Audit;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Credentials;

/// <summary>
/// Add/view/edit/delete/search credentials, and copy a login/password field
/// to the clipboard. Every method that touches a secret field takes the
/// vault credential explicitly and derives the encryption key from it —
/// there is no persistent in-memory session/unlock state here by design;
/// that is STORY-004/005's job (lock/unlock), which this can plug into
/// later without a rewrite. No secret value is ever passed to the audit
/// logger.
///
/// Fully dynamic field model: a credential is a Label plus any number of
/// typed fields (CustomFieldType.Login, .Password, .Website, ...) — there
/// is no fixed "the username field" or "the password field". Copy/Search/
/// health-analysis helpers below pick the first field of a given type,
/// which covers the common case of one login and one password per
/// credential without forcing that shape on the data model.
/// </summary>
public sealed class CredentialService
{
    private readonly CredentialStore _store;
    private readonly VaultEncryptionService _encryption;
    private readonly ICredentialClipboard _clipboard;
    private readonly AuditLogger? _auditLogger;
    private readonly PasswordHistoryService? _passwordHistory;

    public CredentialService(
        CredentialStore store,
        VaultEncryptionService encryption,
        ICredentialClipboard clipboard,
        AuditLogger? auditLogger = null,
        PasswordHistoryService? passwordHistory = null)
    {
        _store = store;
        _encryption = encryption;
        _clipboard = clipboard;
        _auditLogger = auditLogger;
        _passwordHistory = passwordHistory;
    }

    /// <summary>Decrypted list view, every field included — see CredentialView's own remarks on why there's no partial-decrypt list any more.</summary>
    public IReadOnlyList<CredentialView> List(string vaultCredential)
    {
        var key = _encryption.DeriveKey(vaultCredential);
        return _store.GetAll()
            .Select(c => ToView(c, key))
            .ToList();
    }

    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public CredentialView GetById(string vaultCredential, string id)
    {
        var record = FindOrThrow(id);
        var key = _encryption.DeriveKey(vaultCredential);
        return ToView(record, key);
    }

    public string Add(
        string vaultCredential, string label, IReadOnlyList<(string Name, string Value, CustomFieldType Type)> fields,
        bool isFavorite = false, IReadOnlyList<string>? tags = null)
    {
        var key = _encryption.DeriveKey(vaultCredential);
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        _store.Upsert(new Credential(
            id,
            label,
            EncryptFields(fields, key),
            now,
            now,
            IsFavorite: isFavorite,
            Tags: tags));

        _auditLogger?.Log(nameof(Add), Environment.UserName, details: $"id={id}");
        return id;
    }

    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void Edit(
        string vaultCredential, string id, string label, IReadOnlyList<(string Name, string Value, CustomFieldType Type)> fields,
        bool isFavorite = false, IReadOnlyList<string>? tags = null)
    {
        var existing = FindOrThrow(id);
        var key = _encryption.DeriveKey(vaultCredential);

        // Record the outgoing value of any Password-type field whose value
        // is actually changing — before it's overwritten below — so History
        // only ever shows real changes, not a no-op edit (e.g. renaming the
        // credential without touching its password).
        if (_passwordHistory is not null)
        {
            foreach (var newField in fields.Where(f => f.Type == CustomFieldType.Password))
            {
                var oldField = existing.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Password && f.Name == newField.Name);
                if (oldField is null)
                {
                    continue;
                }

                var oldPlain = _encryption.Decrypt(oldField.EncryptedValueBase64, key);
                if (oldPlain.Length > 0 && oldPlain != newField.Value)
                {
                    _passwordHistory.Add(id, newField.Name, oldField.EncryptedValueBase64);
                }
            }
        }

        _store.Upsert(new Credential(
            id,
            label,
            EncryptFields(fields, key),
            DateTimeOffset.UtcNow,
            existing.CreatedAtUtc,
            IsFavorite: isFavorite,
            Tags: tags,
            Icon: existing.Icon));

        _auditLogger?.Log(nameof(Edit), Environment.UserName, details: $"id={id}");
    }

    /// <summary>Sets the credential's icon (symbol/color/custom/website-favicon) without touching any other field.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void SetIcon(string id, CredentialIcon? icon)
    {
        var record = FindOrThrow(id);
        _store.Upsert(record with { Icon = icon, UpdatedAtUtc = DateTimeOffset.UtcNow });
        _auditLogger?.Log(nameof(SetIcon), Environment.UserName, details: $"id={id} kind={icon?.Kind}");
    }

    /// <summary>Replaces the credential's tags without touching any other field — for the detail pane's quick "Set tags" action.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void SetTags(string id, IReadOnlyList<string> tags)
    {
        var record = FindOrThrow(id);
        _store.Upsert(record with { Tags = tags, UpdatedAtUtc = DateTimeOffset.UtcNow });
        _auditLogger?.Log(nameof(SetTags), Environment.UserName, details: $"id={id} tagCount={tags.Count}");
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

    /// <summary>Matches against the label, any tag, or any field's decrypted value — never logs the query text itself.</summary>
    public IReadOnlyList<CredentialView> Search(string vaultCredential, string query)
    {
        var results = List(vaultCredential)
            .Where(v =>
                v.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (v.Tags?.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false) ||
                v.Fields.Any(f => f.Value.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _auditLogger?.Log(nameof(Search), Environment.UserName, details: $"resultCount={results.Count}");
        return results;
    }

    /// <summary>Copies the first Login-type field's value. No-op (empty string) if the credential has none.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    /// <exception cref="ClipboardUnavailableException">The system denied clipboard access.</exception>
    public void CopyUsername(string vaultCredential, string id)
    {
        var view = GetById(vaultCredential, id);
        var value = view.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Login)?.Value ?? string.Empty;
        _clipboard.SetText(value);
        _auditLogger?.Log(nameof(CopyUsername), Environment.UserName, details: $"id={id}");
    }

    /// <summary>Copies the first Password-type field's value. No-op (empty string) if the credential has none.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    /// <exception cref="ClipboardUnavailableException">The system denied clipboard access.</exception>
    public void CopyPassword(string vaultCredential, string id)
    {
        var view = GetById(vaultCredential, id);
        var value = view.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Password)?.Value ?? string.Empty;
        _clipboard.SetText(value);
        _auditLogger?.Log(nameof(CopyPassword), Environment.UserName, details: $"id={id}");
    }

    private CredentialView ToView(Credential record, byte[] key) => new(
        record.Id,
        record.Label,
        record.Fields.Select(f => new CustomFieldView(f.Name, _encryption.Decrypt(f.EncryptedValueBase64, key), f.Type)).ToList(),
        record.UpdatedAtUtc,
        record.CreatedAtUtc,
        IsFavorite: record.IsFavorite,
        Tags: record.Tags ?? Array.Empty<string>(),
        Icon: record.Icon);

    private List<CustomField> EncryptFields(IReadOnlyList<(string Name, string Value, CustomFieldType Type)> fields, byte[] key) =>
        fields.Select(f => new CustomField(f.Name, _encryption.Encrypt(f.Value, key), f.Type)).ToList();

    private Credential FindOrThrow(string id) =>
        _store.GetAll().FirstOrDefault(c => c.Id == id) ?? throw new CredentialNotFoundException(id);
}
