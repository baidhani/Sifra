namespace Sifra.Vault.Autofill;

/// <summary>
/// Thrown when the requested credential does not belong to the page's
/// domain — the "incorrect credentials filled" failure path. Never trust
/// a fill request's credential id blindly; it must actually be one of the
/// credentials offered for that URL.
/// </summary>
public sealed class CredentialDomainMismatchException : Exception
{
    public CredentialDomainMismatchException(string credentialId, string url)
        : base($"Credential '{credentialId}' was not offered for '{url}' and cannot be filled there.")
    {
    }
}
