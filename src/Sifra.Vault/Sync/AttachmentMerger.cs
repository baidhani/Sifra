using Sifra.Vault.Attachments;

namespace Sifra.Vault.Sync;

public enum AttachmentMergeOutcome
{
    Present,
    Deleted,
}

public sealed record AttachmentMergeResult(AttachmentMergeOutcome Outcome, CredentialAttachment? Attachment, DateTimeOffset? DeletedAtUtc);

/// <summary>
/// Merges attachment metadata with deletion tombstones. Unlike credentials,
/// an attachment is immutable once created (CredentialAttachmentService has
/// no "edit" — only Add and Delete), so a given Id's metadata can never
/// legitimately be re-created with new content after being deleted: the Id
/// is minted once, at Add time, and any later appearance of a tombstone for
/// that same Id necessarily postdates the metadata's single, fixed
/// CreatedAtUtc. That means — unlike CredentialTombstoneMerger — a
/// tombstone here ALWAYS wins over a live metadata row for the same Id;
/// there is no "resurrect if edited after deletion" case to weigh, because
/// there is no such thing as editing an attachment after its creation.
/// </summary>
public static class AttachmentMerger
{
    public static AttachmentMergeResult Merge(
        CredentialAttachment? localAttachment, DateTimeOffset? localDeletedAtUtc,
        CredentialAttachment? remoteAttachment, DateTimeOffset? remoteDeletedAtUtc)
    {
        if (localDeletedAtUtc is null && remoteDeletedAtUtc is null)
        {
            // Both sides have the metadata (or only one does, handled by the caller
            // before this is invoked) — nothing was ever deleted, so keep it.
            return new AttachmentMergeResult(AttachmentMergeOutcome.Present, localAttachment ?? remoteAttachment, null);
        }

        var deletedAtUtc = localDeletedAtUtc is not null && remoteDeletedAtUtc is not null
            ? (localDeletedAtUtc.Value > remoteDeletedAtUtc.Value ? localDeletedAtUtc.Value : remoteDeletedAtUtc.Value)
            : (localDeletedAtUtc ?? remoteDeletedAtUtc)!.Value;

        return new AttachmentMergeResult(AttachmentMergeOutcome.Deleted, null, deletedAtUtc);
    }
}
