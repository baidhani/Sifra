namespace Sifra.Vault.Health;

public sealed class BreachCheckResult
{
    public required BreachCheckOutcome Outcome { get; init; }

    /// <summary>Number of times this password has appeared in known breaches. Only set when Outcome is Breached.</summary>
    public int? BreachCount { get; init; }
}
