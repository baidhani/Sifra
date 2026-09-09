namespace Sifra.Vault.Crypto;

/// <summary>
/// One wrapped copy of the vault master key (VMK) — the same VMK, wrapped
/// under a different credential's key-encryption-key (KEK). Multiple
/// slots let more than one credential unlock the same encrypted content
/// (STORY-006: the master password and the recovery key are two separate
/// slots wrapping one shared VMK).
/// </summary>
public sealed record VaultMasterKeySlot(string KekSaltBase64, string WrappedKeyBase64);
