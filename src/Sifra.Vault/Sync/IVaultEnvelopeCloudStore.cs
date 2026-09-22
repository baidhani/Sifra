namespace Sifra.Vault.Sync;

/// <summary>
/// Generic shape any cloud-storage provider must expose for the vault
/// sync envelope: a single revisioned blob, pulled and pushed as a whole.
/// The revision is opaque to VaultSyncService — it only ever compares it
/// for equality — but maps directly onto each real provider's native
/// versioning: Dropbox's `rev`, Google Drive's revision id, OneDrive's
/// ETag. This is what makes TryPush's conditional-write guarantee real
/// rather than a race: the provider itself refuses the write if the
/// revision no longer matches, rather than this code trying to detect
/// staleness on its own.
/// </summary>
public interface IVaultEnvelopeCloudStore
{
    bool IsConnected { get; }

    /// <returns>The envelope and its revision, or null if nothing has ever been published.</returns>
    /// <exception cref="SyncUnavailableException">The provider is not connected.</exception>
    (VaultSyncEnvelope Envelope, string Revision)? Pull();

    /// <summary>
    /// Writes the envelope, but only if the remote's current revision still
    /// equals <paramref name="expectedRevision"/> (null means "nothing has
    /// ever been published yet"). Returns false — instead of throwing or
    /// silently overwriting — when the remote changed since expectedRevision
    /// was read; the caller is expected to re-pull, replay, and retry.
    /// </summary>
    /// <exception cref="SyncUnavailableException">The provider is not connected.</exception>
    bool TryPush(VaultSyncEnvelope envelope, string? expectedRevision, out string newRevision);
}
