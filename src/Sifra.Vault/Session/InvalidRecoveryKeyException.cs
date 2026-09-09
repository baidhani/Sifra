namespace Sifra.Vault.Session;

/// <summary>Thrown when a recovery key does not match the vault's stored hash.</summary>
public sealed class InvalidRecoveryKeyException : Exception
{
    public InvalidRecoveryKeyException()
        : base("This recovery key is not valid for this vault.")
    {
    }
}
