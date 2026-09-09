namespace Sifra.Vault.Crypto;

/// <summary>
/// The persisted, wrapped vault master key (VMK). KekSaltBase64 derives
/// the key-encryption-key (KEK) from the current master password;
/// WrappedKeyBase64 is the VMK encrypted under that KEK. The VMK itself
/// never appears on disk in the clear, and never changes — only its
/// wrapping does when the master password changes (REQ-008).
/// </summary>
public sealed record VaultMasterKeyRecord(string KekSaltBase64, string WrappedKeyBase64);
