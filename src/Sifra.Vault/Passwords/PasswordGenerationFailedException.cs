namespace Sifra.Vault.Passwords;

/// <summary>Thrown when the underlying random source fails — the "password generation fails due to service error" failure path.</summary>
public sealed class PasswordGenerationFailedException : Exception
{
    public PasswordGenerationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
