namespace Sifra.Vault.PCloud;

/// <summary>Thrown when a pCloud call fails for network reasons — the "sync fails with pCloud" failure path.</summary>
public sealed class PCloudNetworkException : Exception
{
    public PCloudNetworkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
