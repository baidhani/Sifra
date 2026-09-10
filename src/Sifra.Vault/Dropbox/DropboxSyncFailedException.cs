namespace Sifra.Vault.Dropbox;

/// <summary>Thrown when a Dropbox sync attempt exhausts its retry budget without succeeding.</summary>
public sealed class DropboxSyncFailedException : Exception
{
    public DropboxSyncFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
