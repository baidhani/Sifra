using Sifra.Vault.Attachments;
using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;
using Sifra.Vault.Tags;

namespace Sifra.Vault.Sync;

/// <summary>
/// Orchestrates one full sync round: pull the remote envelope, merge it
/// against local state field-by-field (CredentialMerger/
/// CredentialTombstoneMerger), apply the merge result locally, then push
/// the merged envelope back — conditioned on the remote not having changed
/// since the pull. A rejected push means another device won the race; this
/// re-pulls and retries the whole merge, capped, rather than retrying
/// forever (this project's ban on unbounded retry loops).
///
/// Merging never touches ciphertext or the vault master key — ciphertext
/// blobs and ciphertext-vs-plaintext comparisons are irrelevant to
/// "which side's timestamp is newer," so SyncNow can run while the vault
/// is locked.
/// </summary>
public sealed class VaultSyncService
{
    private const int MaxPushAttempts = 5;

    /// <summary>
    /// How long a tombstone is kept before this device stops propagating
    /// it. Chosen generously for a personal vault: low-stakes to purge
    /// slightly early or late (see CredentialTombstoneStore.PurgeOlderThan's
    /// remarks) — the worst case is a very-long-offline device
    /// resurrecting an already-deleted item, which the user can simply
    /// delete again. Dropped from the MERGED result before it's applied
    /// locally or pushed (not just purged from local storage afterward) —
    /// purging only the local table would be pointless, since the very
    /// next pull would just merge the still-present remote copy straight
    /// back in.
    /// </summary>
    private static readonly TimeSpan TombstoneRetention = TimeSpan.FromDays(90);

    private readonly CredentialStore _credentialStore;
    private readonly CredentialTombstoneStore _tombstoneStore;
    private readonly TagStore _tagStore;
    private readonly AttachmentStore _attachmentStore;
    private readonly AttachmentTombstoneStore _attachmentTombstoneStore;
    private readonly IVaultEnvelopeCloudStore _cloudStore;
    private readonly AuditLogger? _auditLogger;
    private readonly VaultIdentityStore? _identityStore;

    /// <param name="identityStore">
    /// Optional. When provided, every pull is checked against this
    /// device's own vault identity fingerprint before merging — see
    /// VaultMismatchException. Null (the default) means that safety check
    /// is simply not available; existing callers and tests are unaffected.
    /// </param>
    public VaultSyncService(
        CredentialStore credentialStore,
        CredentialTombstoneStore tombstoneStore,
        TagStore tagStore,
        AttachmentStore attachmentStore,
        AttachmentTombstoneStore attachmentTombstoneStore,
        IVaultEnvelopeCloudStore cloudStore,
        AuditLogger? auditLogger = null,
        VaultIdentityStore? identityStore = null)
    {
        _credentialStore = credentialStore;
        _tombstoneStore = tombstoneStore;
        _tagStore = tagStore;
        _attachmentStore = attachmentStore;
        _attachmentTombstoneStore = attachmentTombstoneStore;
        _cloudStore = cloudStore;
        _auditLogger = auditLogger;
        _identityStore = identityStore;
    }

    /// <exception cref="SyncConflictExhaustedException">Another device kept winning the push race after every retry.</exception>
    /// <exception cref="SyncUnavailableException">The cloud provider is not connected.</exception>
    /// <exception cref="VaultMismatchException">The cloud file/folder belongs to a different vault than this device's.</exception>
    public void SyncNow()
    {
        var localVaultId = _identityStore?.LoadVaultId();

        for (var attempt = 1; attempt <= MaxPushAttempts; attempt++)
        {
            var pulled = _cloudStore.Pull();

            // Both sides must actually know their identity for this check to
            // apply — a null on either side is deliberately permissive (a
            // legacy envelope, or no VaultIdentityStore configured), not
            // treated as "safe to skip verifying." See VaultSyncEnvelope's
            // remarks on VaultId.
            if (localVaultId is not null && pulled?.Envelope.VaultId is { } remoteVaultId && remoteVaultId != localVaultId)
            {
                _auditLogger?.Log(nameof(SyncNow), Environment.UserName, details: "outcome=vault_mismatch");
                throw new VaultMismatchException();
            }

            var tombstoneCutoff = DateTimeOffset.UtcNow - TombstoneRetention;

            var (mergedCredentials, mergedTombstones) = Merge(pulled?.Envelope);
            mergedTombstones = mergedTombstones.Where(t => t.DeletedAtUtc >= tombstoneCutoff).ToList();

            var mergedTags = TagMerger.Merge(_tagStore.GetAll(), pulled?.Envelope.Tags ?? []);

            var (mergedAttachments, mergedAttachmentTombstones) = MergeAttachments(pulled?.Envelope);
            mergedAttachmentTombstones = mergedAttachmentTombstones.Where(t => t.DeletedAtUtc >= tombstoneCutoff).ToList();

            ApplyLocally(mergedCredentials, mergedTombstones, mergedTags, mergedAttachments, mergedAttachmentTombstones);

            // Direct local-table cleanup too — catches rows a local-only
            // vault accumulated without ever syncing, or ones this round's
            // merge never touched (e.g. an id neither side mentioned this
            // time, because both had already dropped it in a prior round).
            _tombstoneStore.PurgeOlderThan(tombstoneCutoff);
            _attachmentTombstoneStore.PurgeOlderThan(tombstoneCutoff);

            var envelope = new VaultSyncEnvelope(mergedCredentials, mergedTombstones, mergedTags, mergedAttachments, mergedAttachmentTombstones, localVaultId);
            if (_cloudStore.TryPush(envelope, pulled?.Revision, out _))
            {
                _auditLogger?.Log(nameof(SyncNow), Environment.UserName, details: $"outcome=success attempt={attempt}");
                return;
            }

            // Another device's push landed between our pull and our push —
            // loop back and merge again against whatever is there now.
        }

        _auditLogger?.Log(nameof(SyncNow), Environment.UserName, details: $"outcome=conflict_exhausted attempts={MaxPushAttempts}");
        throw new SyncConflictExhaustedException(MaxPushAttempts);
    }

    private (List<Credential> Credentials, List<CredentialTombstone> Tombstones) Merge(VaultSyncEnvelope? remote)
    {
        var localCredentials = _credentialStore.GetAll().ToDictionary(c => c.Id);
        var localTombstones = _tombstoneStore.LoadAll().ToDictionary(t => t.Id);
        var remoteCredentials = remote?.Credentials.ToDictionary(c => c.Id) ?? new Dictionary<string, Credential>();
        var remoteTombstones = remote?.Tombstones.ToDictionary(t => t.Id) ?? new Dictionary<string, CredentialTombstone>();

        var allIds = localCredentials.Keys
            .Concat(localTombstones.Keys)
            .Concat(remoteCredentials.Keys)
            .Concat(remoteTombstones.Keys)
            .Distinct();

        var mergedCredentials = new List<Credential>();
        var mergedTombstones = new List<CredentialTombstone>();

        foreach (var id in allIds)
        {
            localCredentials.TryGetValue(id, out var localCredential);
            localTombstones.TryGetValue(id, out var localTombstone);
            remoteCredentials.TryGetValue(id, out var remoteCredential);
            remoteTombstones.TryGetValue(id, out var remoteTombstone);

            var localHasSomething = localCredential is not null || localTombstone is not null;
            var remoteHasSomething = remoteCredential is not null || remoteTombstone is not null;

            CredentialMergeResult result;
            if (localHasSomething && remoteHasSomething)
            {
                result = CredentialTombstoneMerger.Merge(
                    localCredential, localTombstone?.DeletedAtUtc,
                    remoteCredential, remoteTombstone?.DeletedAtUtc);
            }
            else if (localHasSomething)
            {
                result = localCredential is not null
                    ? new CredentialMergeResult(CredentialMergeOutcome.Present, localCredential, null)
                    : new CredentialMergeResult(CredentialMergeOutcome.Deleted, null, localTombstone!.DeletedAtUtc);
            }
            else
            {
                result = remoteCredential is not null
                    ? new CredentialMergeResult(CredentialMergeOutcome.Present, remoteCredential, null)
                    : new CredentialMergeResult(CredentialMergeOutcome.Deleted, null, remoteTombstone!.DeletedAtUtc);
            }

            if (result.Outcome == CredentialMergeOutcome.Present)
            {
                mergedCredentials.Add(result.Credential!);
            }
            else
            {
                mergedTombstones.Add(new CredentialTombstone(id, result.DeletedAtUtc!.Value));
            }
        }

        return (mergedCredentials, mergedTombstones);
    }

    private (List<CredentialAttachment> Attachments, List<AttachmentTombstone> Tombstones) MergeAttachments(VaultSyncEnvelope? remote)
    {
        var localAttachments = _attachmentStore.GetAll().ToDictionary(a => a.Id);
        var localTombstones = _attachmentTombstoneStore.LoadAll().ToDictionary(t => t.Id);
        var remoteAttachments = remote?.Attachments.ToDictionary(a => a.Id) ?? new Dictionary<string, CredentialAttachment>();
        var remoteTombstones = remote?.AttachmentTombstones.ToDictionary(t => t.Id) ?? new Dictionary<string, AttachmentTombstone>();

        var allIds = localAttachments.Keys.Concat(localTombstones.Keys).Concat(remoteAttachments.Keys).Concat(remoteTombstones.Keys).Distinct();

        var mergedAttachments = new List<CredentialAttachment>();
        var mergedTombstones = new List<AttachmentTombstone>();

        foreach (var id in allIds)
        {
            localAttachments.TryGetValue(id, out var localAttachment);
            localTombstones.TryGetValue(id, out var localTombstone);
            remoteAttachments.TryGetValue(id, out var remoteAttachment);
            remoteTombstones.TryGetValue(id, out var remoteTombstone);

            var result = AttachmentMerger.Merge(
                localAttachment, localTombstone?.DeletedAtUtc,
                remoteAttachment, remoteTombstone?.DeletedAtUtc);

            if (result.Outcome == AttachmentMergeOutcome.Present)
            {
                mergedAttachments.Add(result.Attachment!);
            }
            else
            {
                mergedTombstones.Add(new AttachmentTombstone(id, result.DeletedAtUtc!.Value));
            }
        }

        return (mergedAttachments, mergedTombstones);
    }

    private void ApplyLocally(
        List<Credential> mergedCredentials, List<CredentialTombstone> mergedTombstones, List<TagDefinition> mergedTags,
        List<CredentialAttachment> mergedAttachments, List<AttachmentTombstone> mergedAttachmentTombstones)
    {
        foreach (var credential in mergedCredentials)
        {
            _credentialStore.Upsert(credential);
        }

        foreach (var tombstone in mergedTombstones)
        {
            // A deleted credential takes its attachments with it — mirrors
            // CredentialAttachmentService.DeleteAllForCredential, done here
            // directly since sync operates at the store level, below any
            // service's vault-credential/key requirement.
            foreach (var orphaned in _attachmentStore.GetAllForCredential(tombstone.Id))
            {
                mergedAttachmentTombstones.Add(new AttachmentTombstone(orphaned.Id, tombstone.DeletedAtUtc));
            }

            _credentialStore.Delete(tombstone.Id); // no-op if already absent locally
            _tombstoneStore.Add(tombstone.Id, tombstone.DeletedAtUtc);
        }

        _tagStore.Save(mergedTags);

        foreach (var attachment in mergedAttachments)
        {
            // Metadata-only: the blob itself syncs separately (per-file, not
            // yet wired up) — ReadEncryptedBlob will fail until it arrives,
            // same as any not-yet-downloaded file.
            _attachmentStore.UpsertMetadataOnly(attachment);
        }

        foreach (var tombstone in mergedAttachmentTombstones.DistinctBy(t => t.Id))
        {
            _attachmentStore.Delete(tombstone.Id); // no-op if already absent locally
            _attachmentTombstoneStore.Add(tombstone.Id, tombstone.DeletedAtUtc);
        }
    }
}
