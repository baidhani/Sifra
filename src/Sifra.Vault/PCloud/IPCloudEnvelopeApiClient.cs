namespace Sifra.Vault.PCloud;

public sealed record PCloudDownloadResult(byte[] Content, string Hash);

/// <summary>
/// The slice of the pCloud API PCloudVaultEnvelopeCloudStore needs:
/// download-with-hash and conditional (hash-checked) upload. pCloud's
/// "stat" hash field stands in for Dropbox's rev — but unlike Dropbox,
/// pCloud has no documented atomic conditional-write primitive, so the
/// conditional check here is a client-side check-then-write, same
/// documented best-effort limitation as GoogleDriveEnvelopeApiClient (not
/// server-enforced — a narrow race is possible between the check and the
/// write, acceptable for the same reasons already accepted for Google
/// Drive).
/// </summary>
public interface IPCloudEnvelopeApiClient
{
    /// <returns>The file's content and current hash, or null if no file exists at this path yet.</returns>
    /// <exception cref="PCloudNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="PCloudUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<PCloudDownloadResult?> DownloadAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Writes content, but only if the file's current hash still equals
    /// <paramref name="expectedHash"/> (null means "the file must not
    /// exist yet"). Returns null when that condition no longer holds.
    /// </summary>
    /// <returns>The new hash on success, or null if the write was rejected.</returns>
    /// <exception cref="PCloudNetworkException">The request failed for network reasons.</exception>
    /// <exception cref="PCloudUnauthorizedException">The request was rejected as unauthorized.</exception>
    Task<string?> UploadAsync(string path, byte[] content, string? expectedHash, CancellationToken cancellationToken);
}
