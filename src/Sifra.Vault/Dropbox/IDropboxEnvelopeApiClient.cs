namespace Sifra.Vault.Dropbox;

public sealed record DropboxDownloadResult(byte[] Content, string Rev);

/// <summary>
/// The slice of the Dropbox API DropboxVaultEnvelopeCloudStore needs:
/// download-with-revision and conditional (rev-checked) upload. Separate
/// from IDropboxApiClient (the older, upload-only client STORY-014's
/// push-only sync used) — that one has no download or conflict-detection
/// capability, which the envelope sync design requires. Kept as its own
/// interface so DropboxVaultEnvelopeCloudStore is fully unit-testable
/// against a fake, without ever calling the real Dropbox API.
/// </summary>
public interface IDropboxEnvelopeApiClient
{
    /// <returns>The file's content and current revision, or null if no file exists at this path yet.</returns>
    /// <exception cref="DropboxNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="DropboxUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<DropboxDownloadResult?> DownloadAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Writes content, but only if the file's current revision still
    /// equals <paramref name="expectedRev"/> (null means "the file must
    /// not exist yet"). Returns null — instead of throwing — when Dropbox
    /// rejects the write because that condition no longer holds; the
    /// caller is expected to re-pull and retry, not treat this as a
    /// network failure.
    /// </summary>
    /// <returns>The new revision on success, or null if the write was rejected.</returns>
    /// <exception cref="DropboxNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="DropboxUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<string?> UploadAsync(string path, byte[] content, string? expectedRev, CancellationToken cancellationToken);
}
