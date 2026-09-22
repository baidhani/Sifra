namespace Sifra.Vault.Sync;

/// <summary>
/// Generic shape any cloud-storage provider must expose for attachment
/// BLOB bytes — deliberately separate from IVaultEnvelopeCloudStore, per
/// the approved sync design: attachment content transfers as individual
/// per-file uploads/downloads, never embedded in the metadata envelope.
/// Attachments are immutable once created (see AttachmentMerger's
/// remarks), so unlike the envelope there is no revision/conflict concept
/// here — a blob either exists at its Id or it doesn't, and existing
/// content for a given Id never changes.
/// </summary>
public interface ICloudBlobStore
{
    bool IsConnected { get; }

    /// <exception cref="SyncUnavailableException">Not connected.</exception>
    bool Exists(string blobId);

    /// <returns>True and the content if the blob exists remotely; false if not.</returns>
    /// <exception cref="SyncUnavailableException">Not connected.</exception>
    bool TryDownload(string blobId, out byte[] content);

    /// <exception cref="SyncUnavailableException">Not connected.</exception>
    void Upload(string blobId, byte[] content);

    /// <summary>Best-effort — must not throw if the blob is already absent remotely.</summary>
    /// <exception cref="SyncUnavailableException">Not connected.</exception>
    void Delete(string blobId);
}
