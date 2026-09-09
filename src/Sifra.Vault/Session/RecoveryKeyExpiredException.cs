namespace Sifra.Vault.Session;

/// <summary>
/// Thrown when a recovery key is correct but has already been used.
/// Recovery keys are single-use by design — this is what "expired" means
/// in this vault, since there is no time-based expiry concept.
/// </summary>
public sealed class RecoveryKeyExpiredException : Exception
{
    public RecoveryKeyExpiredException()
        : base("This recovery key has already been used and cannot be used again.")
    {
    }
}
