using Sifra.Vault.Attachments;
using Sifra.Vault.Audit;

namespace Sifra.Vault.Sync;

/// <summary>
/// Transfers attachment BLOB bytes to/from the cloud, as individual
/// per-file operations — separate from VaultSyncService, which only
/// merges and pushes attachment METADATA. Run this after VaultSyncService.
/// SyncNow() so metadata for any newly-pulled-in attachments already
/// exists locally (via AttachmentStore.UpsertMetadataOnly) before this
/// looks for their content.
///
/// No merge logic is needed here: attachments are immutable once created,
/// so for any given Id there is only ever one of "upload it" (exists
/// locally, not yet remotely) or "download it" (exists remotely, not yet
/// locally) — never a conflict to resolve.
/// </summary>
public sealed class AttachmentBlobSyncService
{
    private readonly AttachmentStore _attachmentStore;
    private readonly AttachmentTombstoneStore _tombstoneStore;
    private readonly ICloudBlobStore _cloudBlobStore;
    private readonly AuditLogger? _auditLogger;

    public AttachmentBlobSyncService(
        AttachmentStore attachmentStore,
        AttachmentTombstoneStore tombstoneStore,
        ICloudBlobStore cloudBlobStore,
        AuditLogger? auditLogger = null)
    {
        _attachmentStore = attachmentStore;
        _tombstoneStore = tombstoneStore;
        _cloudBlobStore = cloudBlobStore;
        _auditLogger = auditLogger;
    }

    /// <exception cref="SyncUnavailableException">The cloud provider is not connected.</exception>
    public void SyncBlobs()
    {
        if (!_cloudBlobStore.IsConnected)
        {
            throw new SyncUnavailableException();
        }

        var uploaded = 0;
        var downloaded = 0;

        foreach (var attachment in _attachmentStore.GetAll())
        {
            if (!_attachmentStore.HasLocalBlob(attachment.Id))
            {
                // Metadata arrived (via the envelope) before its content did.
                // If the originating device hasn't uploaded the blob yet
                // either, there's nothing to fetch this round — it'll be
                // picked up on a later sync once that device catches up.
                if (_cloudBlobStore.TryDownload(attachment.Id, out var content))
                {
                    _attachmentStore.SaveEncryptedBlob(attachment.Id, content);
                    downloaded++;
                }
                continue;
            }

            if (!_cloudBlobStore.Exists(attachment.Id))
            {
                _cloudBlobStore.Upload(attachment.Id, _attachmentStore.ReadEncryptedBlob(attachment.Id));
                uploaded++;
            }
        }

        foreach (var tombstone in _tombstoneStore.LoadAll())
        {
            _cloudBlobStore.Delete(tombstone.Id); // best-effort, no-op if already absent
        }

        _auditLogger?.Log(nameof(SyncBlobs), Environment.UserName, details: $"uploaded={uploaded} downloaded={downloaded}");
    }
}
