namespace Sifra.Vault.Dropbox;

/// <summary>Thrown when Dropbox rejects the request as unauthorized. Never retried; the credential itself is the problem.</summary>
public sealed class DropboxUnauthorizedException : Exception
{
    public DropboxUnauthorizedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
