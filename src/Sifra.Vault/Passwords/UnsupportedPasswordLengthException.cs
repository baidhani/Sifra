namespace Sifra.Vault.Passwords;

/// <summary>Thrown when a requested password length is outside the supported range.</summary>
public sealed class UnsupportedPasswordLengthException : Exception
{
    public UnsupportedPasswordLengthException(string message)
        : base(message)
    {
    }
}
