namespace Sifra.Vault.Auth;

/// <summary>
/// Generic shape any cloud-storage provider's authentication must expose.
/// STORY-007 (Google Drive) and later providers implement this with real
/// OAuth; nothing in vault authentication depends on which implementation
/// is wired up, or on this interface having been used at all.
/// </summary>
public interface ICloudAuthProvider
{
    bool IsAuthenticated { get; }

    /// <param name="accountIdentifier">Opaque account label (e.g. an email) — never a vault credential.</param>
    /// <exception cref="CloudAuthenticationException">The provider could not authenticate the account.</exception>
    void SignIn(string accountIdentifier);

    void SignOut();
}
