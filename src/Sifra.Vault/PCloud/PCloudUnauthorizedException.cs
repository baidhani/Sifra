namespace Sifra.Vault.PCloud;

/// <summary>Thrown when pCloud rejects the request as unauthorized. Never retried; the credential itself is the problem.</summary>
public sealed class PCloudUnauthorizedException : Exception
{
    public PCloudUnauthorizedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
