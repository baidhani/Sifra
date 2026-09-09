using System.Security.Cryptography;

namespace Sifra.Vault.Passwords;

/// <summary>
/// Real random index source. RandomNumberGenerator.GetInt32 is a BCL
/// method that is already unbiased (rejection sampling internally) — no
/// custom modulo arithmetic needed.
/// </summary>
public sealed class SecureRandomIndexSource : IRandomIndexSource
{
    public int NextInt32(int maxExclusiveValue) => RandomNumberGenerator.GetInt32(maxExclusiveValue);
}
