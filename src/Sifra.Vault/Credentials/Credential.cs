namespace Sifra.Vault.Credentials;

/// <summary>
/// What is persisted for a credential. Username and password are stored
/// only as AES-GCM ciphertext (see VaultEncryptionService) — never in
/// plaintext. Label and Url are kept as plaintext metadata so the list
/// view and search can work without decrypting every record just to
/// render a list; both are lower-sensitivity than the secret fields.
///
/// Same convention extends to the additional fields below: Notes,
/// AccountNumber, Pin, and every CustomField value are secrets (encrypted,
/// like Password) since any of them could hold sensitive data. Phone,
/// IsFavorite, and Labels are plaintext metadata, like Label/Url, since
/// none of them are secrets and the list/filter views need them without
/// a full decrypt.
/// </summary>
public sealed record Credential(
    string Id,
    string Label,
    string EncryptedUsernameBase64,
    string EncryptedPasswordBase64,
    string? Url,
    DateTimeOffset UpdatedAtUtc,
    string? Phone = null,
    string? EncryptedNotesBase64 = null,
    string? EncryptedAccountNumberBase64 = null,
    string? EncryptedPinBase64 = null,
    IReadOnlyList<CustomField>? CustomFields = null,
    bool IsFavorite = false,
    IReadOnlyList<string>? Labels = null);
