namespace Sifra.Vault.Dropbox;

/// <summary>
/// The one narrow slice of the Dropbox API this app needs. Unlike Google
/// Drive/OneDrive, Dropbox upserts by path natively (WriteMode.Overwrite
/// always replaces whatever is at that path, never creates a duplicate),
/// so there is no separate find-then-branch step — just one idempotent
/// upload call. Kept as its own interface so the sync orchestration is
/// fully unit-testable against a fake, without ever calling the real
/// Dropbox API.
/// </summary>
public interface IDropboxApiClient
{
    /// <exception cref="DropboxNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="DropboxUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task UploadAsync(string path, byte[] content, CancellationToken cancellationToken);
}
