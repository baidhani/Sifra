namespace Sifra.Vault.GoogleDrive;

public sealed record GoogleDriveDownloadResult(byte[] Content, string Revision);

/// <summary>
/// The slice of the Drive API GoogleDriveVaultEnvelopeCloudStore needs:
/// download-with-revision and a best-effort conditional upload — separate
/// from IGoogleDriveApiClient (the older, unconditional-upsert client
/// STORY-014's push-only sync used). Kept as its own interface so the
/// store is fully unit-testable against a fake.
///
/// IMPORTANT LIMITATION vs Dropbox: the Google Drive .NET client library
/// exposes no atomic, server-enforced conditional-write primitive for
/// media uploads (no If-Match/ETag support on UpdateMediaUpload, unlike
/// Dropbox's WriteMode.Update(rev), confirmed against the library's
/// actual surface, not assumed). UploadAsync here is therefore a
/// client-side check-then-write: re-read the file's current revision
/// immediately before writing and reject if it no longer matches. This
/// narrows the race window versus a blind overwrite but does not close it
/// atomically — two pushes landing in the same instant could still both
/// pass their own check and race the actual write. Accepted as a
/// documented, low-probability risk for personal-vault use (same
/// risk-acceptance pattern as the clock-skew and tombstone-purge-timing
/// tradeoffs elsewhere in this sync design).
/// </summary>
public interface IGoogleDriveEnvelopeApiClient
{
    /// <returns>The file's content and current revision, or null if no file exists yet.</returns>
    /// <exception cref="GoogleDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="GoogleDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<GoogleDriveDownloadResult?> DownloadAsync(string fileName, CancellationToken cancellationToken);

    /// <returns>The new revision on success, or null if the file's revision no longer matched expectedRevision (null means "must not exist yet").</returns>
    /// <exception cref="GoogleDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="GoogleDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<string?> UploadAsync(string fileName, byte[] content, string? expectedRevision, CancellationToken cancellationToken);
}
