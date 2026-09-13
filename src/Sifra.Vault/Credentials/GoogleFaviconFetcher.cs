namespace Sifra.Vault.Credentials;

/// <summary>
/// Fetches a favicon via Google's public favicon proxy (no API key,
/// resolves the actual icon for us instead of us having to parse each
/// site's HTML for a &lt;link rel="icon"&gt;). Same failure-handling shape as
/// HibpBreachChecker: a bounded timeout, and any failure returns null
/// rather than throwing — a missing favicon must never crash a save.
/// </summary>
public sealed class GoogleFaviconFetcher : IFaviconFetcher
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _httpClient;

    public GoogleFaviconFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<byte[]?> FetchAsync(string websiteUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(websiteUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            // 256px. 512 was tried and empirically looked WORSE for at least
            // one real site (GitHub) — Google's favicon proxy apparently
            // doesn't just transparently upscale its best source for larger
            // sz values as expected; for some domains a larger request
            // yields a different, lower-quality result. 256 is the last
            // verified-good size (crisp for GitHub/Reddit/Stack Overflow in
            // live testing), so trust that over the untested theory.
            var faviconUrl = $"https://www.google.com/s2/favicons?sz=256&domain={Uri.EscapeDataString(uri.Host)}";
            using var response = await _httpClient.GetAsync(faviconUrl, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                    && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
