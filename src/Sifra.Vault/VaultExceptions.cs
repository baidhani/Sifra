namespace Sifra.Vault;

/// <summary>
/// Thrown when a caller tries to create a vault while one already exists on this device.
/// STORY-001 acceptance: the user should be prompted to use the existing vault instead.
/// </summary>
public sealed class VaultAlreadyExistsException : Exception
{
    public VaultAlreadyExistsException()
        : base("A vault already exists on this device. Use the existing vault instead of creating a new one.")
    {
    }
}

/// <summary>
/// Thrown when the vault file cannot be read or written, e.g. the storage
/// location is unavailable or not writable. Wraps the underlying I/O error.
/// </summary>
public sealed class VaultStorageException : Exception
{
    public VaultStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
