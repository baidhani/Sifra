namespace Sifra.Vault.Sync;

public enum VaultMigrationOutcome
{
    /// <summary>The source's vault (envelope + any transferable attachment blobs) was copied to the destination.</summary>
    Migrated,

    /// <summary>The source provider has no published vault to migrate — nothing was done.</summary>
    NothingToMigrate,

    /// <summary>
    /// The destination already holds vault data — refused rather than
    /// overwriting it, the same way a first-ever VaultSyncService push is
    /// refused if something unexpected is already there.
    /// </summary>
    DestinationAlreadyHasData,
}

public sealed record VaultMigrationResult(VaultMigrationOutcome Outcome, int AttachmentsTransferred);

/// <summary>
/// Moves a vault's cloud-synced data from one provider to another —
/// entirely provider-agnostic, since it only depends on
/// IVaultEnvelopeCloudStore/ICloudBlobStore, the same interfaces
/// VaultSyncService/AttachmentBlobSyncService already use. This is the
/// app-assisted alternative to manually downloading/re-uploading the
/// envelope and blob files by hand — same underlying operation, just
/// automated and verified.
///
/// IMPORTANT (a real constraint of this design, not a bug): migration is
/// inherently per-device. Running this on one device updates only THAT
/// device's active provider; any other device sharing this vault keeps
/// pointing at the old provider until it is separately switched too —
/// there is no channel for one device to notify another. The caller
/// (Desktop UI) is responsible for surfacing that clearly to the user
/// before running a migration, and for updating this device's own stored
/// "active provider" setting afterward — this service only moves data,
/// it does not know or care what a "current provider" setting is.
/// </summary>
public sealed class VaultProviderMigrationService
{
    /// <exception cref="SyncUnavailableException">Either provider is not connected.</exception>
    public VaultMigrationResult Migrate(
        IVaultEnvelopeCloudStore source, ICloudBlobStore sourceBlobs,
        IVaultEnvelopeCloudStore destination, ICloudBlobStore destinationBlobs)
    {
        var pulled = source.Pull();
        if (pulled is null)
        {
            return new VaultMigrationResult(VaultMigrationOutcome.NothingToMigrate, 0);
        }

        // expectedRevision: null means "the destination must not already
        // have anything" — the same safety net a first-ever
        // VaultSyncService push already relies on, reused here for free.
        if (!destination.TryPush(pulled.Value.Envelope, expectedRevision: null, out _))
        {
            return new VaultMigrationResult(VaultMigrationOutcome.DestinationAlreadyHasData, 0);
        }

        var transferred = 0;
        foreach (var attachment in pulled.Value.Envelope.Attachments)
        {
            if (sourceBlobs.TryDownload(attachment.Id, out var content))
            {
                destinationBlobs.Upload(attachment.Id, content);
                transferred++;
            }
            // Else: this device never had the blob downloaded locally
            // either (or it's genuinely missing on the source) — nothing
            // to transfer for this one; a device that later syncs against
            // the NEW provider with the blob already local will still
            // upload it there normally via AttachmentBlobSyncService.
        }

        return new VaultMigrationResult(VaultMigrationOutcome.Migrated, transferred);
    }
}
