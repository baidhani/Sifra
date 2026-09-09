namespace Sifra.Vault.Credentials;

/// <summary>Thrown when an operation targets a credential id that does not exist.</summary>
public sealed class CredentialNotFoundException : Exception
{
    public CredentialNotFoundException(string id)
        : base($"No credential found with id '{id}'.")
    {
    }
}
