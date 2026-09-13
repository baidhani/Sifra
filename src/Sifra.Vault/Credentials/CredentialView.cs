namespace Sifra.Vault.Credentials;

/// <summary>
/// Decrypted, read-only view of a credential. Fields are fully decrypted
/// here (both from List and GetById) — with a dynamic field model there is
/// no single "safe" field left to expose without decrypting, so list and
/// detail views carry the same data. Label/IsFavorite/Tags are plaintext
/// metadata and are always present.
/// </summary>
public sealed record CredentialView(
    string Id,
    string Label,
    IReadOnlyList<CustomFieldView> Fields,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset CreatedAtUtc,
    bool IsFavorite = false,
    IReadOnlyList<string>? Tags = null,
    CredentialIcon? Icon = null);
