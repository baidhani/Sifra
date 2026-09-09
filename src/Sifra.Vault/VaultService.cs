using System.Security.Cryptography;

namespace Sifra.Vault;

/// <summary>
/// Vault creation. Owns the rule that a device has exactly one vault, and
/// that a recovery key is generated once and never persisted in plaintext.
/// </summary>
public sealed class VaultService
{
    private const int SaltLengthBytes = 16;
    private const int HashLengthBytes = 32;
    private const int Pbkdf2Iterations = 210_000; // OWASP current minimum for PBKDF2-SHA256

    private readonly VaultStore _store;

    public VaultService(VaultStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Creates a new vault on this device and returns the plaintext recovery
    /// key. This is the only moment the recovery key exists in plaintext —
    /// the caller is responsible for showing it to the user immediately;
    /// it cannot be recovered from the vault afterward.
    /// </summary>
    /// <exception cref="VaultAlreadyExistsException">
    /// A vault already exists on this device.
    /// </exception>
    /// <exception cref="VaultStorageException">
    /// The vault could not be persisted (e.g. storage unavailable).
    /// </exception>
    public string CreateVault()
    {
        if (_store.Exists())
        {
            throw new VaultAlreadyExistsException();
        }

        var recoveryKey = RecoveryKeyGenerator.Generate();

        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password: recoveryKey,
            salt: salt,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashLengthBytes);

        var record = new VaultRecord(
            CreatedAtUtc: DateTimeOffset.UtcNow,
            RecoveryKeySaltBase64: Convert.ToBase64String(salt),
            RecoveryKeyHashBase64: Convert.ToBase64String(hash));

        _store.Save(record);

        return recoveryKey;
    }
}
