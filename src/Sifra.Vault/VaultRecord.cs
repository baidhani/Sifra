namespace Sifra.Vault;

/// <summary>
/// What is persisted for a vault. The master password is never stored, and the
/// recovery key is stored only as a salted hash — never in plaintext.
/// </summary>
public sealed record VaultRecord(
    DateTimeOffset CreatedAtUtc,
    string RecoveryKeySaltBase64,
    string RecoveryKeyHashBase64);
