namespace Sifra.Vault.Health;

/// <summary>
/// Checks whether a password has appeared in a known breach. Takes the
/// plaintext password only to hash it locally — implementations must
/// never transmit the plaintext or the full hash over the network (see
/// HibpBreachChecker's k-anonymity approach).
/// </summary>
public interface IBreachChecker
{
    Task<BreachCheckResult> CheckAsync(string password, CancellationToken cancellationToken);
}
