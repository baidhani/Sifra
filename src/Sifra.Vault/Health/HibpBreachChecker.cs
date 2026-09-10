using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Sifra.Vault.Health;

/// <summary>
/// Live breach check against Have I Been Pwned's Pwned Passwords range API.
/// Uses k-anonymity: only the first 5 hex characters of the password's
/// SHA-1 hash are sent; the plaintext and full hash never leave this
/// process. No API key or account is required for this endpoint.
///
/// Failure handling: transient failures (timeout, 5xx) retry up to
/// MaxAttempts with a fixed delay; a non-transient failure (4xx, or
/// retries exhausted) returns CheckUnavailable rather than throwing or
/// silently reporting "not breached" — a network hiccup must never look
/// like a clean bill of health.
/// </summary>
public sealed class HibpBreachChecker : IBreachChecker
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;

    public HibpBreachChecker(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BreachCheckResult> CheckAsync(string password, CancellationToken cancellationToken)
    {
        var fullHash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = fullHash[..5];
        var suffix = fullHash[5..];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RequestTimeout);

                using var response = await _httpClient.GetAsync(
                    $"https://api.pwnedpasswords.com/range/{prefix}", timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
                    {
                        await Task.Delay(RetryDelay, cancellationToken);
                        continue;
                    }
                    return new BreachCheckResult { Outcome = BreachCheckOutcome.CheckUnavailable };
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return ParseResponse(body, suffix);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                        && !cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                    continue;
                }
                return new BreachCheckResult { Outcome = BreachCheckOutcome.CheckUnavailable };
            }
        }

        return new BreachCheckResult { Outcome = BreachCheckOutcome.CheckUnavailable };
    }

    private static BreachCheckResult ParseResponse(string body, string suffix)
    {
        foreach (var line in body.Split('\n'))
        {
            var parts = line.Trim().Split(':');
            if (parts.Length != 2) continue;

            if (string.Equals(parts[0], suffix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[1], out var count))
            {
                return new BreachCheckResult { Outcome = BreachCheckOutcome.Breached, BreachCount = count };
            }
        }

        return new BreachCheckResult { Outcome = BreachCheckOutcome.NotBreached };
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;
}
