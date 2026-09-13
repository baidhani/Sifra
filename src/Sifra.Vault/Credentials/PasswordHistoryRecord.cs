namespace Sifra.Vault.Credentials;

/// <summary>
/// One previous value of a Password-type field, kept when that field is
/// edited to a new value. Stores the same AES-GCM ciphertext the field
/// itself used — no new crypto path, no re-encryption needed since the old
/// value is simply carried over as-is before being overwritten.
/// </summary>
public sealed record PasswordHistoryRecord(
    string CredentialId,
    string FieldName,
    string EncryptedValueBase64,
    DateTimeOffset ChangedAtUtc);
