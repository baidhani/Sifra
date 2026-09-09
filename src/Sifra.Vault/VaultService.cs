using System.Security.Cryptography;
using Sifra.Vault.Audit;

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
    private readonly AuditLogger? _auditLogger;

    /// <param name="auditLogger">
    /// Optional (STORY-015). When provided, a successful CreateVault call is
    /// logged to the trust-spine audit trail. Null means no audit logging —
    /// existing callers and tests are unaffected.
    /// </param>
    public VaultService(VaultStore store, AuditLogger? auditLogger = null)
    {
        _store = store;
        _auditLogger = auditLogger;
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
        var hash = HashRecoveryKey(recoveryKey, salt);

        var record = new VaultRecord(
            CreatedAtUtc: DateTimeOffset.UtcNow,
            RecoveryKeySaltBase64: Convert.ToBase64String(salt),
            RecoveryKeyHashBase64: Convert.ToBase64String(hash));

        _store.Save(record);

        _auditLogger?.Log(nameof(CreateVault), Environment.UserName);

        return recoveryKey;
    }

    /// <summary>
    /// Checks a candidate recovery key against the stored hash. Does not
    /// check whether the key has already been consumed — see
    /// <see cref="IsRecoveryKeyConsumed"/> — since "wrong key" and
    /// "right key but already used" are different, separately reportable
    /// failure conditions (STORY-006).
    /// </summary>
    public bool VerifyRecoveryKey(string candidateRecoveryKey)
    {
        var record = _store.Load();
        if (record is null)
        {
            return false;
        }

        var salt = Convert.FromBase64String(record.RecoveryKeySaltBase64);
        var expectedHash = Convert.FromBase64String(record.RecoveryKeyHashBase64);
        var actualHash = HashRecoveryKey(candidateRecoveryKey, salt);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    public bool IsRecoveryKeyConsumed() => _store.Load()?.RecoveryKeyConsumedAtUtc is not null;

    /// <summary>
    /// Marks the recovery key as used — it is single-use by design (see
    /// the original data model: "consumed on recovery").
    /// </summary>
    public void ConsumeRecoveryKey()
    {
        var record = _store.Load() ?? throw new InvalidOperationException("No vault exists to consume a recovery key for.");
        _store.Save(record with { RecoveryKeyConsumedAtUtc = DateTimeOffset.UtcNow });
    }

    private static byte[] HashRecoveryKey(string recoveryKey, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(recoveryKey, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashLengthBytes);
}
