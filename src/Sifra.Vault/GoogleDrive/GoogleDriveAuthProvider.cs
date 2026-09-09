using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;
using Sifra.Vault.Auth;

namespace Sifra.Vault.GoogleDrive;

/// <summary>
/// Real ICloudAuthProvider implementation (STORY-013's seam) using
/// Google's OAuth 2.0 installed-app flow. Entirely separate from vault
/// authentication (VaultAuthenticator) — this only ever handles the
/// Google account side, per the project guardrail.
/// </summary>
public sealed class GoogleDriveAuthProvider : ICloudAuthProvider
{
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tokenStoreDirectory;

    public GoogleDriveAuthProvider(string clientId, string clientSecret, string tokenStoreDirectory)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _tokenStoreDirectory = tokenStoreDirectory;
    }

    public bool IsAuthenticated { get; private set; }

    public UserCredential? Credential { get; private set; }

    /// <param name="accountIdentifier">The Google account email to authorize.</param>
    /// <exception cref="CloudAuthenticationException">Google sign-in failed.</exception>
    public void SignIn(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier))
        {
            throw new ArgumentException("Account identifier is required.", nameof(accountIdentifier));
        }

        try
        {
            Credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
                new[] { DriveService.Scope.DriveFile },
                accountIdentifier,
                CancellationToken.None,
                new FileDataStore(_tokenStoreDirectory, fullPath: true)).GetAwaiter().GetResult();

            IsAuthenticated = true;
        }
        catch (Exception ex)
        {
            throw new CloudAuthenticationException($"Google sign-in failed for '{accountIdentifier}': {ex.Message}");
        }
    }

    public void SignOut()
    {
        Credential = null;
        IsAuthenticated = false;
    }
}
