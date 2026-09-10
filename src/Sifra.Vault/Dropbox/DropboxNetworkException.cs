namespace Sifra.Vault.Dropbox;

/// <summary>Thrown when a Dropbox call fails for network reasons — the "sync fails with Dropbox" failure path.</summary>
public sealed class DropboxNetworkException : Exception
{
    public DropboxNetworkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
