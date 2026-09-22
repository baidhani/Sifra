using Sifra.Vault.Attachments;
using Sifra.Vault.Credentials;
using Sifra.Vault.Tags;

namespace Sifra.Vault.Sync;

/// <summary>
/// The unit of data pulled from and pushed to the cloud for one sync round
/// — credentials, their deletion tombstones, the shared tag registry, and
/// attachment metadata (with its own tombstones). Attachment BLOB BYTES
/// never belong here — they sync as separate per-file uploads, per the
/// approved design — this only carries the metadata row (filename, kind,
/// size, id). Password history and devices are excluded from sync entirely.
/// </summary>
/// <param name="VaultId">
/// This vault's identity fingerprint (see VaultEncryptionService.ComputeVaultId),
/// set on every push. VaultSyncService compares this against the local
/// device's own fingerprint before merging a pulled envelope in, refusing
/// with VaultMismatchException if they differ — see its remarks. Default
/// null only for envelopes from before this field existed, or a device
/// with no VaultIdentityStore configured; a null VaultId is deliberately
/// exempt from the check (see VaultSyncService), not a stand-in for "no vault."
/// </param>
public sealed record VaultSyncEnvelope(
    IReadOnlyList<Credential> Credentials,
    IReadOnlyList<CredentialTombstone> Tombstones,
    IReadOnlyList<TagDefinition> Tags,
    IReadOnlyList<CredentialAttachment> Attachments,
    IReadOnlyList<AttachmentTombstone> AttachmentTombstones,
    string? VaultId = null);
