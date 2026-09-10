namespace Sifra.Vault.Health;

public enum BreachCheckOutcome
{
    /// <summary>Password's hash suffix was not found in the breach corpus.</summary>
    NotBreached,

    /// <summary>Password's hash suffix was found — it has appeared in known breaches.</summary>
    Breached,

    /// <summary>
    /// The check could not be completed (network failure, timeout, or an
    /// unexpected response) after retries were exhausted. Distinct from
    /// NotBreached on purpose — the caller must not treat "couldn't check"
    /// as "safe", which would be a false negative baked into the API.
    /// </summary>
    CheckUnavailable,
}
