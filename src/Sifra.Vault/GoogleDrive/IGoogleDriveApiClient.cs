namespace Sifra.Vault.GoogleDrive;

/// <summary>
/// The one narrow slice of the Google Drive API this app needs: find and
/// upsert a single named file. Kept separate from GoogleDriveSyncProvider
/// so the sync orchestration (encryption, retry, idempotent upsert logic)
/// is fully unit-testable against a fake, without ever calling the real
/// Google SDK or network in tests.
/// </summary>
public interface IGoogleDriveApiClient
{
    /// <returns>The Drive file id if a file with this name already exists, otherwise null.</returns>
    /// <exception cref="GoogleDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="GoogleDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<string?> FindFileIdAsync(string fileName, CancellationToken cancellationToken);

    /// <summary>Creates the file if existingFileId is null, otherwise replaces its content — never creates a second file with the same name.</summary>
    /// <returns>The Drive file id (same as existingFileId when updating).</returns>
    /// <exception cref="GoogleDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="GoogleDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<string> UploadOrUpdateFileAsync(string fileName, string? existingFileId, byte[] content, CancellationToken cancellationToken);
}
