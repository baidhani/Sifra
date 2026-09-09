namespace Sifra.Vault.Passwords;

/// <summary>
/// Abstraction over the random index source used to pick characters and
/// shuffle. Lets tests inject a failing source to exercise the "service
/// error" failure path without relying on the real RNG ever failing.
/// </summary>
public interface IRandomIndexSource
{
    /// <returns>A uniformly random integer in [0, maxExclusiveValue).</returns>
    int NextInt32(int maxExclusiveValue);
}
