namespace Sifra.Vault.Credentials;

/// <summary>
/// Decrypted, read-only view of a credential. Password is null in list
/// results (List/Search) and populated only by GetById — a list dump
/// should not decrypt and expose every password at once.
/// </summary>
public sealed record CredentialView(
    string Id,
    string Label,
    string Username,
    string? Password,
    string? Url,
    DateTimeOffset UpdatedAtUtc);
