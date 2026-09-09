namespace Sifra.Vault.Session;

/// <summary>Thrown when a caller tries to use the unlocked credential while the vault is locked.</summary>
public sealed class VaultLockedException : Exception
{
    public VaultLockedException()
        : base("The vault is locked. Unlock it before accessing credentials.")
    {
    }
}
