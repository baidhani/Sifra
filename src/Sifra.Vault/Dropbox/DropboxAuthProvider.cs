using System.Diagnostics;
using System.Net;
using global::Dropbox.Api;
using Sifra.Vault.Auth;

namespace Sifra.Vault.Dropbox;

/// <summary>
/// Real ICloudAuthProvider implementation (STORY-013's seam) using
/// Dropbox's OAuth 2.0 PKCE flow (no app secret needed — App key only,
/// same as Google/OneDrive's public-client model). Runs a local loopback
/// HTTP listener to catch the redirect, mirroring what MSAL/Google's
/// libraries do internally. Entirely separate from vault authentication,
/// per the project guardrail.
/// </summary>
public sealed class DropboxAuthProvider : ICloudAuthProvider
{
    private const int RedirectPort = 52475;
    private static readonly Uri RedirectUri = new($"http://localhost:{RedirectPort}/");

    private readonly string _appKey;

    public DropboxAuthProvider(string appKey)
    {
        _appKey = appKey;
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
                tokenAccessType: TokenAccessType.Online,
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
            IsAuthenticated = true;
        }
        catch (Exception ex)
        {
            throw new CloudAuthenticationException($"Dropbox sign-in failed for '{accountIdentifier}': {ex.Message}");
        }
    }

    public void SignOut()
    {
        AccessToken = null;
        IsAuthenticated = false;
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
