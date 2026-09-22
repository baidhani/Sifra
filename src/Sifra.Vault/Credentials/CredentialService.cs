using Sifra.Vault.Audit;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;

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
    private readonly CredentialTombstoneStore? _tombstones;

    /// <param name="tombstones">
    /// Optional (Phase 3). When provided, Delete records a tombstone
    /// instead of just removing the row silently — see
    /// CredentialTombstoneStore's remarks. Null means no sync is
    /// configured; existing callers and tests are unaffected.
    /// </param>
    public CredentialService(
        CredentialStore store,
        VaultEncryptionService encryption,
        ICredentialClipboard clipboard,
        AuditLogger? auditLogger = null,
        PasswordHistoryService? passwordHistory = null,
        CredentialTombstoneStore? tombstones = null)
    {
        _store = store;
        _encryption = encryption;
        _clipboard = clipboard;
        _auditLogger = auditLogger;
        _passwordHistory = passwordHistory;
        _tombstones = tombstones;
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
            EncryptFields(fields, key, existingFields: null, now),
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
        var now = DateTimeOffset.UtcNow;

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
            EncryptFields(fields, key, existing.Fields, now),
            now,
            existing.CreatedAtUtc,
            IsFavorite: isFavorite,
            Tags: tags,
            Icon: existing.Icon,
            IsArchived: existing.IsArchived,
            IsDeleted: existing.IsDeleted,
            DeletedAtUtc: existing.DeletedAtUtc,
            IsLocked: existing.IsLocked));

        _auditLogger?.Log(nameof(Edit), Environment.UserName, details: $"id={id}");
    }

    /// <summary>
    /// Creates an independent copy of a credential — new id, "(copy)"
    /// appended to the label, same fields/tags/icon metadata — but never
    /// favorite/locked/archived/deleted: a duplicate always starts as a
    /// fresh, ordinary item regardless of the original's state. Field
    /// values are copied as their already-encrypted bytes (the vault's
    /// encryption key isn't per-credential, so no decrypt/re-encrypt round
    /// trip is needed).
    /// </summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public string Duplicate(string id)
    {
        var existing = FindOrThrow(id);
        var now = DateTimeOffset.UtcNow;
        var newId = Guid.NewGuid().ToString("N");

        _store.Upsert(new Credential(
            newId,
            existing.Label + " (copy)",
            existing.Fields.Select(f => f with { UpdatedAtUtc = now }).ToList(),
            now,
            now,
            IsFavorite: false,
            Tags: existing.Tags,
            Icon: existing.Icon));

        _auditLogger?.Log(nameof(Duplicate), Environment.UserName, details: $"id={id} newId={newId}");
        return newId;
    }

    /// <summary>Copies a plain-text summary of the credential (label + every non-empty field) to the clipboard.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    /// <exception cref="ClipboardUnavailableException">The system denied clipboard access.</exception>
    public void CopyAsText(string vaultCredential, string id)
    {
        _clipboard.SetText(BuildPlainTextSummary(GetById(vaultCredential, id)));
        _auditLogger?.Log(nameof(CopyAsText), Environment.UserName, details: $"id={id}");
    }

    /// <summary>Same plain-text summary as CopyAsText, returned directly for writing to a file instead of the clipboard — see VaultView's Export action.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public string ExportAsText(string vaultCredential, string id)
    {
        var text = BuildPlainTextSummary(GetById(vaultCredential, id));
        _auditLogger?.Log(nameof(ExportAsText), Environment.UserName, details: $"id={id}");
        return text;
    }

    private static string BuildPlainTextSummary(CredentialView view)
    {
        var lines = new List<string> { view.Label };
        lines.AddRange(view.Fields.Where(f => f.Value.Length > 0).Select(f => $"{f.Name}: {f.Value}"));
        return string.Join(Environment.NewLine, lines);
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

    /// <summary>Toggles IsArchived without needing every other field — same pattern as SetFavorite.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void SetArchived(string id, bool isArchived)
    {
        var record = FindOrThrow(id);
        _store.Upsert(record with { IsArchived = isArchived, UpdatedAtUtc = DateTimeOffset.UtcNow });
        _auditLogger?.Log(nameof(SetArchived), Environment.UserName, details: $"id={id} isArchived={isArchived}");
    }

    /// <summary>
    /// Toggles IsLocked without needing every other field — same pattern
    /// as SetFavorite. This just flips the flag; enforcing what a locked
    /// item blocks, and the re-authenticate-to-unlock flow, is the Desktop
    /// UI's job (see Credential's own remarks).
    /// </summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void SetLocked(string id, bool isLocked)
    {
        var record = FindOrThrow(id);
        _store.Upsert(record with { IsLocked = isLocked, UpdatedAtUtc = DateTimeOffset.UtcNow });
        _auditLogger?.Log(nameof(SetLocked), Environment.UserName, details: $"id={id} isLocked={isLocked}");
    }

    /// <summary>
    /// Moves a credential to Trash — sets IsDeleted/DeletedAtUtc without
    /// touching anything else, so it's fully recoverable via Restore. This
    /// is what the normal Delete action in the UI calls; it does NOT
    /// remove data or create a sync tombstone (see Delete below for that).
    /// </summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void SoftDelete(string id)
    {
        var record = FindOrThrow(id);
        _store.Upsert(record with { IsDeleted = true, DeletedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow });
        _auditLogger?.Log(nameof(SoftDelete), Environment.UserName, details: $"id={id}");
    }

    /// <summary>Restores a Trash item — the inverse of SoftDelete.</summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void Restore(string id)
    {
        var record = FindOrThrow(id);
        _store.Upsert(record with { IsDeleted = false, DeletedAtUtc = null, UpdatedAtUtc = DateTimeOffset.UtcNow });
        _auditLogger?.Log(nameof(Restore), Environment.UserName, details: $"id={id}");
    }

    /// <summary>
    /// Permanently destroys a credential — real removal plus a sync
    /// tombstone, so every device eventually deletes its copy too. Reserved
    /// for emptying the Trash; the normal in-app Delete action is
    /// SoftDelete, not this.
    /// </summary>
    /// <exception cref="CredentialNotFoundException">No credential exists with this id.</exception>
    public void Delete(string id)
    {
        FindOrThrow(id);
        _tombstones?.Add(id, DateTimeOffset.UtcNow);
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
        record.Fields.Select(f => new CustomFieldView(f.Name, _encryption.Decrypt(f.EncryptedValueBase64, key), f.Type, f.UpdatedAtUtc)).ToList(),
        record.UpdatedAtUtc,
        record.CreatedAtUtc,
        IsFavorite: record.IsFavorite,
        Tags: record.Tags ?? Array.Empty<string>(),
        Icon: record.Icon,
        IsArchived: record.IsArchived,
        IsDeleted: record.IsDeleted,
        DeletedAtUtc: record.DeletedAtUtc,
        IsLocked: record.IsLocked);

    /// <summary>
    /// Stamps each field with the current edit time, except a field whose
    /// plaintext value is unchanged from what's already stored — that one
    /// keeps its previous UpdatedAtUtc rather than being bumped just
    /// because the credential as a whole was re-saved (e.g. renaming the
    /// credential without touching this field). This is the groundwork
    /// Phase 3's per-field cloud-sync versioning depends on: a field's
    /// timestamp must reflect when its value actually last changed, not
    /// merely when some Edit() call happened to touch the credential.
    /// </summary>
    private List<CustomField> EncryptFields(
        IReadOnlyList<(string Name, string Value, CustomFieldType Type)> fields,
        byte[] key,
        IReadOnlyList<CustomField>? existingFields,
        DateTimeOffset now)
    {
        return fields.Select(f =>
        {
            var existing = existingFields?.FirstOrDefault(e => e.Name == f.Name && e.Type == f.Type);
            var updatedAt = now;
            if (existing is not null && _encryption.Decrypt(existing.EncryptedValueBase64, key) == f.Value)
            {
                updatedAt = existing.UpdatedAtUtc;
            }

            return new CustomField(f.Name, _encryption.Encrypt(f.Value, key), f.Type, updatedAt);
        }).ToList();
    }

    private Credential FindOrThrow(string id) =>
        _store.GetAll().FirstOrDefault(c => c.Id == id) ?? throw new CredentialNotFoundException(id);
}
