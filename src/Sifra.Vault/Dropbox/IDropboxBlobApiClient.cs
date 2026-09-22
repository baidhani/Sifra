namespace Sifra.Vault.Dropbox;

/// <summary>
/// The slice of the Dropbox API DropboxCloudBlobStore needs: existence
/// check, download, upload, and best-effort delete — for individual
/// attachment blob files, separate from IDropboxEnvelopeApiClient (which
/// only ever handles the single metadata envelope file and its
/// revision-conditional writes). Blobs are immutable once uploaded, so
/// there is no revision concept here at all.
/// </summary>
public interface IDropboxBlobApiClient
{
    /// <exception cref="DropboxNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="DropboxUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);

    /// <returns>The content, or null if no file exists at this path.</returns>
    /// <exception cref="DropboxNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="DropboxUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<byte[]?> DownloadAsync(string path, CancellationToken cancellationToken);

    /// <exception cref="DropboxNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="DropboxUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task UploadAsync(string path, byte[] content, CancellationToken cancellationToken);

    /// <summary>Best-effort — must not throw if the file is already absent.</summary>
    /// <exception cref="DropboxNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="DropboxUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task DeleteAsync(string path, CancellationToken cancellationToken);
}
