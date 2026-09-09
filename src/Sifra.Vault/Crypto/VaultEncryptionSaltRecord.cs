namespace Sifra.Vault.Crypto;

/// <summary>
/// The persisted salt used to derive the vault's encryption key. Separate
/// file, separate salt from VaultAccessCredentialStore (STORY-013) — the
/// same vault credential derives two cryptographically independent
/// outputs (an auth verifier hash, and this encryption key) because the
/// salts differ.
/// </summary>
public sealed record VaultEncryptionSaltRecord(string SaltBase64);
