namespace Sifra.Vault.PCloud;

/// <summary>Thrown when a pCloud sync attempt exhausts its retry budget without succeeding.</summary>
public sealed class PCloudSyncFailedException : Exception
{
    public PCloudSyncFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
