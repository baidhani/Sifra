namespace Sifra.Vault.GoogleDrive;

/// <summary>The slice of the Drive API GoogleDriveCloudBlobStore needs: existence, download, upload, best-effort delete for individual attachment blob files — see IDropboxBlobApiClient's remarks for the equivalent Dropbox shape. No revision concept: blobs are immutable once uploaded.</summary>
public interface IGoogleDriveBlobApiClient
{
    /// <exception cref="GoogleDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="GoogleDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<bool> ExistsAsync(string fileName, CancellationToken cancellationToken);

    /// <returns>The content, or null if no file exists with this name.</returns>
    /// <exception cref="GoogleDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="GoogleDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<byte[]?> DownloadAsync(string fileName, CancellationToken cancellationToken);

    /// <exception cref="GoogleDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="GoogleDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task UploadAsync(string fileName, byte[] content, CancellationToken cancellationToken);

    /// <summary>Best-effort — must not throw if the file is already absent.</summary>
    /// <exception cref="GoogleDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="GoogleDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task DeleteAsync(string fileName, CancellationToken cancellationToken);
}
