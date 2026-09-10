using Sifra.Vault.Health;

namespace Sifra.Vault.Tests;

/// <summary>Test double — no network. Configure exactly what each password should return.</summary>
public sealed class FakeBreachChecker : IBreachChecker
{
    private readonly Dictionary<string, BreachCheckResult> _results = new();
    public int CallCount { get; private set; }

    public FakeBreachChecker WithResult(string password, BreachCheckResult result)
    {
        _results[password] = result;
        return this;
    }

    public Task<BreachCheckResult> CheckAsync(string password, CancellationToken cancellationToken)
    {
        CallCount++;
        var result = _results.TryGetValue(password, out var configured)
            ? configured
            : new BreachCheckResult { Outcome = BreachCheckOutcome.NotBreached };
        return Task.FromResult(result);
    }
}
