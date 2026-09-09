namespace Sifra.Vault.Session;

/// <summary>Thrown when a requested new master password does not meet the minimum security criteria.</summary>
public sealed class WeakMasterPasswordException : Exception
{
    public WeakMasterPasswordException(string message)
        : base(message)
    {
    }
}
