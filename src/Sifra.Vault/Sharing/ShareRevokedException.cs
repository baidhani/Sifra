namespace Sifra.Vault.Sharing;

/// <summary>
/// Thrown when a recipient tries to access a share that has been revoked.
/// The ciphertext the recipient may have already copied elsewhere is
/// unaffected by this — revocation cuts off the app's own access path
/// going forward, not anything already outside it. That limitation is
/// inherent to any offline sharing scheme, not specific to this one.
/// </summary>
public sealed class ShareRevokedException : Exception
{
    public ShareRevokedException(string shareId)
        : base($"Share '{shareId}' has been revoked and can no longer be accessed.")
    {
    }
}
