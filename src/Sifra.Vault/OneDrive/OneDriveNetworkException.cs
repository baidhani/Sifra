namespace Sifra.Vault.OneDrive;

/// <summary>Thrown when a OneDrive/Graph call fails for network reasons — the "sync fails with OneDrive" failure path.</summary>
public sealed class OneDriveNetworkException : Exception
{
    public OneDriveNetworkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
