using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;
using Sifra.Vault.Auth;

namespace Sifra.Vault.GoogleDrive;

/// <summary>
/// Real ICloudAuthProvider implementation (STORY-013's seam) using
/// Google's OAuth 2.0 installed-app flow. Entirely separate from vault
/// authentication (VaultAuthenticator) — this only ever handles the
/// Google account side, per the project guardrail.
///
/// Persistent local storage is a fixed key (not the real account email)
/// in a FileDataStore under tokenStoreDirectory — Google's own client
/// library already persists the refresh token there and reuses it
/// silently inside SignIn()/AuthorizeAsync() on its own; TrySilentSignIn
/// exists specifically for callers (like a background timer) that must
/// NEVER risk opening a browser, which AuthorizeAsync can still do if the
/// stored token is missing or its refresh fails.
/// </summary>
public sealed class GoogleDriveAuthProvider : ICloudAuthProvider
{
    private const string StoredAccountKey = "sifra-vault-user";

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

    /// <param name="accountIdentifier">Only used for logging/UX context, same as DropboxAuthProvider — the actual local token storage key is fixed (StoredAccountKey), so TrySilentSignIn always knows where to look regardless of what's passed here.</param>
    /// <exception cref="CloudAuthenticationException">Google sign-in failed.</exception>
    public void SignIn(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier))
        {
            throw new ArgumentException("Account identifier is required.", nameof(accountIdentifier));
        }

        try
        {
            // AuthorizeAsync already reuses a valid stored token silently on
            // its own (Google's library checks the DataStore first) — this
            // call only ever opens a browser when no valid token is stored
            // yet, or a stored one can no longer be refreshed.
            Credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
                new[] { DriveService.Scope.DriveFile },
                StoredAccountKey,
                CancellationToken.None,
                new FileDataStore(_tokenStoreDirectory, fullPath: true)).GetAwaiter().GetResult();

            IsAuthenticated = true;
        }
        catch (Exception ex)
        {
            throw new CloudAuthenticationException($"Google sign-in failed for '{accountIdentifier}': {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to reconnect using a previously-saved token, without ever
    /// risking an interactive browser prompt: loads the stored token
    /// directly and refreshes it via a plain HTTP call — no fallback path
    /// here can open a browser, unlike SignIn()/AuthorizeAsync().
    /// </summary>
    /// <returns>True if reconnected; false if there's no stored token, or it's no longer valid (caller should fall back to interactive SignIn).</returns>
    public bool TrySilentSignIn()
    {
        if (IsAuthenticated)
        {
            return true;
        }

        try
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
                Scopes = [DriveService.Scope.DriveFile],
                DataStore = new FileDataStore(_tokenStoreDirectory, fullPath: true),
            });

            var token = flow.LoadTokenAsync(StoredAccountKey, CancellationToken.None).GetAwaiter().GetResult();
            if (token is null)
            {
                return false;
            }

            var credential = new UserCredential(flow, StoredAccountKey, token);
            var refreshed = credential.RefreshTokenAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (!refreshed)
            {
                return false;
            }

            Credential = credential;
            IsAuthenticated = true;
            return true;
        }
        catch (Exception)
        {
            Credential = null;
            IsAuthenticated = false;
            return false;
        }
    }

    public void SignOut()
    {
        Credential = null;
        IsAuthenticated = false;
    }
}
