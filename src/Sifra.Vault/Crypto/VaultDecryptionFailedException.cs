namespace Sifra.Vault.Crypto;

/// <summary>
/// Thrown when a ciphertext cannot be decrypted with the given key — most
/// often because the wrong vault credential was supplied (AES-GCM's
/// authentication tag check fails), not necessarily file corruption.
/// </summary>
public sealed class VaultDecryptionFailedException : Exception
{
    public VaultDecryptionFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
