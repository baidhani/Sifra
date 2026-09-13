namespace Sifra.Vault.Credentials;

/// <summary>
/// What is persisted for a credential. Fully dynamic field model: every
/// piece of secret data (login, password, website, phone, notes, PIN,
/// anything else) is just an entry in Fields, typed via CustomFieldType,
/// and every field value is stored only as AES-GCM ciphertext (see
/// VaultEncryptionService) — never in plaintext. There is no fixed
/// "Username"/"Password"/"Url" shape any more; a credential can have any
/// number of fields of any type, added and removed freely.
///
/// Label, IsFavorite, and Tags remain fixed, plaintext metadata — not
/// fields — since they identify and organize the credential itself rather
/// than describing an account, and the list/filter views need them without
/// a full decrypt.
/// </summary>
public sealed record Credential(
    string Id,
    string Label,
    IReadOnlyList<CustomField> Fields,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset CreatedAtUtc,
    bool IsFavorite = false,
    IReadOnlyList<string>? Tags = null,
    CredentialIcon? Icon = null);
