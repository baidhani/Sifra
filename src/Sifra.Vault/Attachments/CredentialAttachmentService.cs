using Sifra.Vault.Audit;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Attachments;

/// <summary>
/// Add/list/read/delete file and image attachments for a credential. Same
/// shape as CredentialService: every method that touches attachment
/// content takes the vault credential explicitly and derives the
/// encryption key from it — no persistent in-memory session here either.
/// File bytes are never logged; only metadata (filename, size, kind).
/// </summary>
public sealed class CredentialAttachmentService
{
    private readonly AttachmentStore _store;
    private readonly VaultEncryptionService _encryption;
    private readonly AuditLogger? _auditLogger;
    private readonly AttachmentTombstoneStore? _tombstones;

    /// <param name="tombstones">
    /// Optional (Phase 3). When provided, Delete/DeleteAllForCredential
    /// record a tombstone instead of just removing the row silently — see
    /// AttachmentTombstoneStore's remarks. Null means no sync is
    /// configured; existing callers and tests are unaffected.
    /// </param>
    public CredentialAttachmentService(
        AttachmentStore store,
        VaultEncryptionService encryption,
        AuditLogger? auditLogger = null,
        AttachmentTombstoneStore? tombstones = null)
    {
        _store = store;
        _encryption = encryption;
        _auditLogger = auditLogger;
        _tombstones = tombstones;
    }

    public IReadOnlyList<CredentialAttachment> List(string credentialId) =>
        _store.GetAllForCredential(credentialId);

    public string Add(string vaultCredential, string credentialId, string fileName, AttachmentKind kind, byte[] fileBytes)
    {
        var key = _encryption.DeriveKey(vaultCredential);
        var id = Guid.NewGuid().ToString("N");
        var encrypted = _encryption.EncryptBytes(fileBytes, key);

        var metadata = new CredentialAttachment(id, credentialId, fileName, kind, fileBytes.LongLength, DateTimeOffset.UtcNow);
        _store.Add(metadata, encrypted);

        _auditLogger?.Log(nameof(Add), Environment.UserName, details: $"credentialId={credentialId} attachmentId={id} fileName={fileName} kind={kind} sizeBytes={fileBytes.LongLength}");
        return id;
    }

    /// <exception cref="VaultDecryptionFailedException">The vault credential is wrong.</exception>
    public byte[] GetDecryptedBytes(string vaultCredential, string attachmentId)
    {
        var key = _encryption.DeriveKey(vaultCredential);
        var encrypted = _store.ReadEncryptedBlob(attachmentId);
        return _encryption.DecryptBytes(encrypted, key);
    }

    public void Delete(string attachmentId)
    {
        _tombstones?.Add(attachmentId, DateTimeOffset.UtcNow);
        _store.Delete(attachmentId);
        _auditLogger?.Log(nameof(Delete), Environment.UserName, details: $"attachmentId={attachmentId}");
    }

    /// <summary>Called alongside CredentialService.Delete so a deleted credential's attachments don't become orphaned files.</summary>
    public void DeleteAllForCredential(string credentialId)
    {
        if (_tombstones is not null)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var attachment in _store.GetAllForCredential(credentialId))
            {
                _tombstones.Add(attachment.Id, now);
            }
        }

        _store.DeleteAllForCredential(credentialId);
        _auditLogger?.Log(nameof(DeleteAllForCredential), Environment.UserName, details: $"credentialId={credentialId}");
    }
}
