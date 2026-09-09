namespace Sifra.Vault.GoogleDrive;

/// <summary>Thrown when Google Drive rejects the request as unauthorized — the "unauthorized access to Google Drive" failure path. Never retried; the credential itself is the problem.</summary>
public sealed class GoogleDriveUnauthorizedException : Exception
{
    public GoogleDriveUnauthorizedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
