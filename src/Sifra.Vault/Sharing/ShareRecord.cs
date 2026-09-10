namespace Sifra.Vault.Sharing;

/// <summary>
/// Persisted, at-rest form of a share. Username/password are only ever
/// present as ciphertext here — encrypted under a key derived from the
/// recipient's passphrase, via KekSaltBase64, never the vault's own key.
/// </summary>
public sealed record ShareRecord(
    string Id,
    string RecipientLabel,
    string Label,
    string? Url,
    string KekSaltBase64,
    string EncryptedUsernameBase64,
    string EncryptedPasswordBase64,
    DateTimeOffset CreatedAtUtc,
    bool Revoked,
    DateTimeOffset? RevokedAtUtc);
