namespace Sifra.Vault.OneDrive;

/// <summary>
/// The one narrow slice of the Microsoft Graph API this app needs: find
/// and upsert a single named file in the app's dedicated OneDrive folder
/// (the "app folder" — matches the Files.ReadWrite.AppFolder scope, the
/// least-privilege equivalent of Google Drive's drive.file). Kept separate
/// from OneDriveSyncProvider so the sync orchestration is fully
/// unit-testable against a fake, without ever calling the real Graph API.
/// </summary>
public interface IOneDriveApiClient
{
    /// <returns>The Graph item id if a file with this name already exists in the app folder, otherwise null.</returns>
    /// <exception cref="OneDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="OneDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<string?> FindFileIdAsync(string fileName, CancellationToken cancellationToken);

    /// <summary>Creates the file if existingItemId is null, otherwise replaces its content — never creates a second file with the same name.</summary>
    /// <returns>The Graph item id (same as existingItemId when updating).</returns>
    /// <exception cref="OneDriveNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="OneDriveUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<string> UploadOrUpdateFileAsync(string fileName, string? existingItemId, byte[] content, CancellationToken cancellationToken);
}
