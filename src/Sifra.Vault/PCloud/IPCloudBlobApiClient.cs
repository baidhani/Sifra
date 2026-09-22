namespace Sifra.Vault.PCloud;

/// <summary>
/// The slice of the pCloud API PCloudCloudBlobStore needs: existence
/// check, download, upload, and best-effort delete — for individual
/// attachment blob files. Blobs are immutable once uploaded, so there is
/// no hash/conflict concept here at all (mirrors IDropboxBlobApiClient).
/// </summary>
public interface IPCloudBlobApiClient
{
    /// <exception cref="PCloudNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="PCloudUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);

    /// <returns>The content, or null if no file exists at this path.</returns>
    /// <exception cref="PCloudNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="PCloudUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<byte[]?> DownloadAsync(string path, CancellationToken cancellationToken);

    /// <exception cref="PCloudNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="PCloudUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task UploadAsync(string path, byte[] content, CancellationToken cancellationToken);

    /// <summary>Best-effort — must not throw if the file is already absent.</summary>
    /// <exception cref="PCloudNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="PCloudUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task DeleteAsync(string path, CancellationToken cancellationToken);
}
