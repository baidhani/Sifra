namespace Sifra.Vault;

/// <summary>
/// What is persisted for a vault. The master password is never stored, and the
/// recovery key is stored only as a salted hash — never in plaintext.
/// </summary>
/// <param name="RecoveryKeyConsumedAtUtc">
/// Null until the recovery key is used (STORY-006). The recovery key is
/// single-use — matches the original data model design ("consumed on
/// recovery"). Additive field: older persisted vault.json files without
/// it deserialize with this defaulting to null.
/// </param>
public sealed record VaultRecord(
    DateTimeOffset CreatedAtUtc,
    string RecoveryKeySaltBase64,
    string RecoveryKeyHashBase64,
    DateTimeOffset? RecoveryKeyConsumedAtUtc = null);
