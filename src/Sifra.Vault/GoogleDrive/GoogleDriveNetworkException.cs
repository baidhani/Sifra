namespace Sifra.Vault.GoogleDrive;

/// <summary>Thrown when a Google Drive call fails for network reasons — the "sync fails due to network issues" failure path.</summary>
public sealed class GoogleDriveNetworkException : Exception
{
    public GoogleDriveNetworkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
