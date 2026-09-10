namespace Sifra.Vault.OneDrive;

/// <summary>Thrown when a OneDrive sync attempt exhausts its retry budget without succeeding.</summary>
public sealed class OneDriveSyncFailedException : Exception
{
    public OneDriveSyncFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
