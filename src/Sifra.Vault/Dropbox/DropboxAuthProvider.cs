using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using global::Dropbox.Api;
using Sifra.Vault.Auth;

namespace Sifra.Vault.Dropbox;

/// <summary>
/// Real ICloudAuthProvider implementation (STORY-013's seam) using
/// Dropbox's OAuth 2.0 PKCE flow (no app secret needed — App key only,
/// same as Google/OneDrive's public-client model). Runs a local loopback
/// HTTP listener to catch the redirect, mirroring what MSAL/Google's
/// libraries do internally. Entirely separate from vault authentication,
/// per the project guardrail. Windows-only since persistent silent
/// reconnect (TrySilentSignIn) relies on DropboxTokenStore's DPAPI
/// encryption — matches this app's actual deployment target (Sifra.Desktop
/// is a WPF app, net10.0-windows already).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DropboxAuthProvider : ICloudAuthProvider
{
    private const int RedirectPort = 52475;
    private static readonly Uri RedirectUri = new($"http://localhost:{RedirectPort}/");

    private readonly string _appKey;
    private readonly DropboxTokenStore _tokenStore;

    private string? _refreshToken;
    private DateTime _accessTokenExpiresAtUtc = DateTime.MinValue;

    public DropboxAuthProvider(string appKey, DropboxTokenStore? tokenStore = null)
    {
        _appKey = appKey;
        _tokenStore = tokenStore ?? new DropboxTokenStore();
    }

    public bool IsAuthenticated { get; private set; }

    public string? AccessToken { get; private set; }

    /// <param name="accountIdentifier">Only used for logging/UX context — Dropbox's flow itself doesn't take a login hint.</param>
    /// <exception cref="CloudAuthenticationException">Dropbox sign-in failed.</exception>
    public void SignIn(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier))
        {
            throw new ArgumentException("Account identifier is required.", nameof(accountIdentifier));
        }

        try
        {
            var codeVerifier = DropboxOAuth2Helper.GeneratePKCECodeVerifier();
            var codeChallenge = DropboxOAuth2Helper.GeneratePKCECodeChallenge(codeVerifier);
            var state = Guid.NewGuid().ToString("N");

            var authorizeUri = DropboxOAuth2Helper.GetAuthorizeUri(
                oauthResponseType: OAuthResponseType.Code,
                clientId: _appKey,
                redirectUri: RedirectUri,
                state: state,
                // Offline access requests a refresh token alongside the
                // access token, so TrySilentSignIn can reconnect on a later
                // app launch without opening a browser again.
                tokenAccessType: TokenAccessType.Offline,
                codeChallenge: codeChallenge);

            using var listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri.ToString());
            listener.Start();

            OpenBrowser(authorizeUri.ToString());

            var context = listener.GetContext(); // blocks until the redirect arrives
            var receivedQuery = ParseQueryString(context.Request.Url!.Query);
            RespondToBrowser(context);
            listener.Stop();

            if (!receivedQuery.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException("No authorization code received from Dropbox.");
            }
            if (receivedQuery.TryGetValue("state", out var returnedState) && returnedState != state)
            {
                throw new InvalidOperationException("OAuth state mismatch — possible interception.");
            }

            var tokenResult = DropboxOAuth2Helper.ProcessCodeFlowAsync(
                    code: code,
                    appKey: _appKey,
                    appSecret: null!,
                    redirectUri: RedirectUri.ToString(),
                    codeVerifier: codeVerifier)
                .GetAwaiter().GetResult();

            AccessToken = tokenResult.AccessToken;
            _refreshToken = tokenResult.RefreshToken;
            _accessTokenExpiresAtUtc = tokenResult.ExpiresAt ?? DateTime.MinValue;
            IsAuthenticated = true;

            if (!string.IsNullOrEmpty(_refreshToken))
            {
                _tokenStore.SaveRefreshToken(_refreshToken);
            }
        }
        catch (Exception ex)
        {
            throw new CloudAuthenticationException($"Dropbox sign-in failed for '{accountIdentifier}': {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to reconnect using a previously-saved refresh token,
    /// without opening a browser. Validates the token with one cheap API
    /// call (also forcing an immediate token refresh, since the access
    /// token here is deliberately treated as already-expired) so a
    /// revoked/invalid stored token is detected immediately rather than
    /// surfacing as a confusing failure on the first real sync.
    /// </summary>
    /// <returns>True if reconnected; false if there's no stored token, or it's no longer valid (caller should fall back to interactive SignIn).</returns>
    public bool TrySilentSignIn()
    {
        if (IsAuthenticated)
        {
            return true;
        }

        var refreshToken = _tokenStore.LoadRefreshToken();
        if (string.IsNullOrEmpty(refreshToken))
        {
            return false;
        }

        _refreshToken = refreshToken;
        AccessToken = string.Empty;
        _accessTokenExpiresAtUtc = DateTime.MinValue; // already-expired -> forces an immediate refresh below

        try
        {
            // Not CreateClient() — it requires IsAuthenticated, which is
            // exactly what this method is still in the middle of
            // establishing. BuildClient bypasses that guard intentionally.
            using var client = BuildClient(TimeSpan.FromSeconds(15));
            client.Users.GetCurrentAccountAsync().GetAwaiter().GetResult();
            IsAuthenticated = true;
            return true;
        }
        catch (Exception)
        {
            _refreshToken = null;
            AccessToken = null;
            IsAuthenticated = false;
            return false;
        }
    }

    /// <summary>Builds a DropboxClient using this provider's current tokens — auto-refreshes internally (via the SDK) whenever a refresh token is present and the access token has expired.</summary>
    /// <exception cref="InvalidOperationException">Not signed in.</exception>
    public DropboxClient CreateClient(TimeSpan? timeout = null)
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("Not signed in to Dropbox.");
        }

        return BuildClient(timeout);
    }

    private DropboxClient BuildClient(TimeSpan? timeout)
    {
        var config = new DropboxClientConfig("Sifra") { HttpClient = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) } };

        return !string.IsNullOrEmpty(_refreshToken)
            ? new DropboxClient(AccessToken ?? string.Empty, _refreshToken, _accessTokenExpiresAtUtc, _appKey, config)
            : new DropboxClient(AccessToken!, config);
    }

    public void SignOut()
    {
        AccessToken = null;
        _refreshToken = null;
        _accessTokenExpiresAtUtc = DateTime.MinValue;
        IsAuthenticated = false;
        _tokenStore.Clear();
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void RespondToBrowser(HttpListenerContext context)
    {
        const string html = "<html><body>You can close this window and return to Sifra.</body></html>";
        var buffer = System.Text.Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.OutputStream.Close();
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>();
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&'))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }
        return result;
    }
}
