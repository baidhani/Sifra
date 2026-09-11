namespace Sifra.Vault.Credentials;

/// <summary>
/// Decrypted, read-only view of a credential. Secret fields (Password,
/// Notes, AccountNumber, Pin, CustomFields) are null/empty in list results
/// (List/Search) and populated only by GetById — a list dump should not
/// decrypt and expose every secret at once. Phone/IsFavorite/Labels are
/// plaintext metadata and are always present, like Label/Url.
/// </summary>
public sealed record CredentialView(
    string Id,
    string Label,
    string Username,
    string? Password,
    string? Url,
    DateTimeOffset UpdatedAtUtc,
    string? Phone = null,
    string? Notes = null,
    string? AccountNumber = null,
    string? Pin = null,
    IReadOnlyList<CustomFieldView>? CustomFields = null,
    bool IsFavorite = false,
    IReadOnlyList<string>? Labels = null);
