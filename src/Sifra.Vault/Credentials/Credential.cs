namespace Sifra.Vault.Credentials;

/// <summary>
/// What is persisted for a credential. Username and password are stored
/// only as AES-GCM ciphertext (see VaultEncryptionService) — never in
/// plaintext. Label and Url are kept as plaintext metadata so the list
/// view and search can work without decrypting every record just to
/// render a list; both are lower-sensitivity than the secret fields.
/// </summary>
public sealed record Credential(
    string Id,
    string Label,
    string EncryptedUsernameBase64,
    string EncryptedPasswordBase64,
    string? Url,
    DateTimeOffset UpdatedAtUtc);
