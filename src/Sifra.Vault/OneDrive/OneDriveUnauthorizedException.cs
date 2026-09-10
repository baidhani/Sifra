namespace Sifra.Vault.OneDrive;

/// <summary>Thrown when Microsoft Graph rejects the request as unauthorized. Never retried; the credential itself is the problem.</summary>
public sealed class OneDriveUnauthorizedException : Exception
{
    public OneDriveUnauthorizedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
