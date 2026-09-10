using Microsoft.Identity.Client;
using Sifra.Vault.Auth;

namespace Sifra.Vault.OneDrive;

/// <summary>
/// Real ICloudAuthProvider implementation (STORY-013's seam) using MSAL's
/// public-client OAuth flow against Microsoft identity platform. Entirely
/// separate from vault authentication, per the project guardrail.
/// </summary>
public sealed class OneDriveAuthProvider : ICloudAuthProvider
{
    private static readonly string[] Scopes = { "Files.ReadWrite.AppFolder" };

    private readonly IPublicClientApplication _app;
    private readonly string _tokenCacheFilePath;

    public OneDriveAuthProvider(string clientId, string tokenCacheFilePath)
    {
        _tokenCacheFilePath = tokenCacheFilePath;
        _app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
            .WithDefaultRedirectUri()
            .Build();

        _app.UserTokenCache.SetBeforeAccess(BeforeAccessNotification);
        _app.UserTokenCache.SetAfterAccess(AfterAccessNotification);
    }

    public bool IsAuthenticated { get; private set; }

    public string? AccessToken { get; private set; }

    /// <param name="accountIdentifier">The Microsoft account email to authorize.</param>
    /// <exception cref="CloudAuthenticationException">Microsoft sign-in failed.</exception>
    public void SignIn(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier))
        {
            throw new ArgumentException("Account identifier is required.", nameof(accountIdentifier));
        }

        try
        {
            var accounts = _app.GetAccountsAsync().GetAwaiter().GetResult();
            var existingAccount = accounts.FirstOrDefault(a =>
                string.Equals(a.Username, accountIdentifier, StringComparison.OrdinalIgnoreCase));

            AuthenticationResult result;
            try
            {
                if (existingAccount is null)
                {
                    throw new MsalUiRequiredException("no_cached_account", "No cached account for this identifier.");
                }
                result = _app.AcquireTokenSilent(Scopes, existingAccount).ExecuteAsync().GetAwaiter().GetResult();
            }
            catch (MsalUiRequiredException)
            {
                result = _app.AcquireTokenInteractive(Scopes)
                    .WithLoginHint(accountIdentifier)
                    .ExecuteAsync().GetAwaiter().GetResult();
            }

            AccessToken = result.AccessToken;
            IsAuthenticated = true;
        }
        catch (Exception ex)
        {
            throw new CloudAuthenticationException($"Microsoft sign-in failed for '{accountIdentifier}': {ex.Message}");
        }
    }

    public void SignOut()
    {
        var accounts = _app.GetAccountsAsync().GetAwaiter().GetResult();
        foreach (var account in accounts)
        {
            _app.RemoveAsync(account).GetAwaiter().GetResult();
        }

        AccessToken = null;
        IsAuthenticated = false;
    }

    private void BeforeAccessNotification(TokenCacheNotificationArgs args)
    {
        if (File.Exists(_tokenCacheFilePath))
        {
            args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(_tokenCacheFilePath));
        }
    }

    private void AfterAccessNotification(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_tokenCacheFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllBytes(_tokenCacheFilePath, args.TokenCache.SerializeMsalV3());
    }
}
