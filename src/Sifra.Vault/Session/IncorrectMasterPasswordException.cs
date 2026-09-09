namespace Sifra.Vault.Session;

/// <summary>Thrown when the current master password supplied to a change-password attempt is wrong.</summary>
public sealed class IncorrectMasterPasswordException : Exception
{
    public IncorrectMasterPasswordException()
        : base("The current password is incorrect.")
    {
    }
}
