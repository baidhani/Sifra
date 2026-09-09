namespace Sifra.Vault.GoogleDrive;

/// <summary>Thrown when a sync attempt exhausts its retry budget without succeeding.</summary>
public sealed class GoogleDriveSyncFailedException : Exception
{
    public GoogleDriveSyncFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
