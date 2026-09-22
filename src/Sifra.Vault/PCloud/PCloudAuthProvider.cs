using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;
using Sifra.Vault.Auth;

namespace Sifra.Vault.PCloud;

/// <summary>
/// Real ICloudAuthProvider implementation for pCloud, using its classic
/// OAuth 2.0 code flow (docs.pcloud.com/methods/oauth_2.0/) — unlike
/// Dropbox/Google, pCloud's flow uses a client SECRET rather than PKCE (no
/// public-client/PKCE support documented), so this app's pCloud app
/// registration must supply both an ID and secret. Runs the same local
/// loopback HTTP listener pattern as DropboxAuthProvider to catch the
/// redirect. pCloud has two data-center regions (US: api.pcloud.com, EU:
/// eapi.pcloud.com) — which one a given account's token works against is
/// returned by the token endpoint itself, so this provider surfaces
/// ApiHost for callers (RealPCloudApiClient) to use instead of a hardcoded
/// host. Windows-only for the same DPAPI-backed persistence reason as
/// DropboxAuthProvider.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PCloudAuthProvider : ICloudAuthProvider
{
    private const int RedirectPort = 53877;
    private static readonly Uri RedirectUri = new($"http://localhost:{RedirectPort}/");
    private static readonly HttpClient HttpClient = new();

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly PCloudTokenStore _tokenStore;

    public PCloudAuthProvider(string clientId, string clientSecret, PCloudTokenStore? tokenStore = null)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _tokenStore = tokenStore ?? new PCloudTokenStore();
    }

    public bool IsAuthenticated { get; private set; }

    public string? AccessToken { get; private set; }

    /// <summary>The region-specific API host this account's token must be used against (e.g. "api.pcloud.com" or "eapi.pcloud.com").</summary>
    public string? ApiHost { get; private set; }

    /// <param name="accountIdentifier">Only used for error-message context — pCloud's flow itself doesn't take a login hint.</param>
    /// <exception cref="CloudAuthenticationException">pCloud sign-in failed.</exception>
    public void SignIn(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier))
        {
            throw new ArgumentException("Account identifier is required.", nameof(accountIdentifier));
        }

        try
        {
            var state = Guid.NewGuid().ToString("N");
            var authorizeUri = $"https://my.pcloud.com/oauth2/authorize?client_id={Uri.EscapeDataString(_clientId)}" +
                $"&response_type=code&redirect_uri={Uri.EscapeDataString(RedirectUri.ToString())}&state={state}";

            using var listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri.ToString());
            listener.Start();

            OpenBrowser(authorizeUri);

            var context = listener.GetContext(); // blocks until the redirect arrives
            var receivedQuery = ParseQueryString(context.Request.Url!.Query);
            RespondToBrowser(context);
            listener.Stop();

            if (!receivedQuery.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException("No authorization code received from pCloud.");
            }
            if (receivedQuery.TryGetValue("state", out var returnedState) && returnedState != state)
            {
                throw new InvalidOperationException("OAuth state mismatch — possible interception.");
            }

            var (accessToken, apiHost) = ExchangeCodeForTokenAsync(code).GetAwaiter().GetResult();

            AccessToken = accessToken;
            ApiHost = apiHost;
            IsAuthenticated = true;
            _tokenStore.SaveToken(new PCloudStoredToken(accessToken, apiHost));
        }
        catch (Exception ex)
        {
            throw new CloudAuthenticationException($"pCloud sign-in failed for '{accountIdentifier}': {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to reconnect using a previously-saved access token, without
    /// opening a browser. pCloud tokens issued via this flow don't expire
    /// under normal use, so this just validates the stored token is still
    /// accepted rather than refreshing anything.
    /// </summary>
    /// <returns>True if reconnected; false if there's no stored token, or it's no longer valid (caller should fall back to interactive SignIn).</returns>
    public bool TrySilentSignIn()
    {
        if (IsAuthenticated)
        {
            return true;
        }

        var stored = _tokenStore.LoadToken();
        if (stored is null)
        {
            return false;
        }

        try
        {
            using var response = HttpClient.GetAsync(
                $"https://{stored.ApiHost}/userinfo?auth={Uri.EscapeDataString(stored.AccessToken)}").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (doc.RootElement.TryGetProperty("result", out var result) && result.GetInt32() == 0)
            {
                AccessToken = stored.AccessToken;
                ApiHost = stored.ApiHost;
                IsAuthenticated = true;
                return true;
            }
        }
        catch (Exception)
        {
            // Falls through to the failure return below.
        }

        return false;
    }

    public void SignOut()
    {
        AccessToken = null;
        ApiHost = null;
        IsAuthenticated = false;
        _tokenStore.Clear();
    }

    private async Task<(string AccessToken, string ApiHost)> ExchangeCodeForTokenAsync(string code)
    {
        // No PKCE/public-client mode documented for pCloud — the client
        // secret travels in this request, same tolerance as Google's
        // installed-app OAuth client secret (not truly secret for a
        // desktop app, but that's pCloud's only supported flow).
        using var response = await HttpClient.GetAsync(
            $"https://api.pcloud.com/oauth2_token?client_id={Uri.EscapeDataString(_clientId)}" +
            $"&client_secret={Uri.EscapeDataString(_clientSecret)}&code={Uri.EscapeDataString(code)}");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        if (!json.TryGetProperty("result", out var result) || result.GetInt32() != 0)
        {
            var errorText = json.TryGetProperty("error", out var err) ? err.GetString() : "unknown error";
            throw new InvalidOperationException($"pCloud token exchange failed: {errorText}");
        }

        var accessToken = json.GetProperty("access_token").GetString()!;
        // "hostname" identifies which of pCloud's two regional API hosts
        // this account's token must be used against — falls back to the US
        // host if the field is absent (undocumented in the excerpts
        // available at build time; confirm against a live account).
        var apiHost = json.TryGetProperty("hostname", out var host) ? host.GetString()! : "api.pcloud.com";

        return (accessToken, apiHost);
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
