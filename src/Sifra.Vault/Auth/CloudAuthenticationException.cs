namespace Sifra.Vault.Auth;

/// <summary>
/// Thrown when a cloud-storage provider fails to authenticate an account.
/// Never affects vault authentication — the two are deliberately unrelated.
/// </summary>
public sealed class CloudAuthenticationException : Exception
{
    public CloudAuthenticationException(string message)
        : base(message)
    {
    }
}
