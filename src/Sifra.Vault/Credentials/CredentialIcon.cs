namespace Sifra.Vault.Credentials;

public enum CredentialIconKind
{
    /// <summary>Default: the credential's own initial letter on a colored background.</summary>
    InitialLetter,
    /// <summary>An image fetched from the credential's Website field.</summary>
    WebsiteFavicon,
    /// <summary>A built-in Fluent System Icon symbol on a colored background.</summary>
    Symbol,
    /// <summary>A user-supplied image file.</summary>
    Custom,
}

/// <summary>
/// How a credential's avatar/icon should render. Plaintext metadata, like
/// Label/IsFavorite/Tags — not a secret. The actual image bytes for
/// WebsiteFavicon/Custom are stored separately (see CredentialIconImageStore)
/// since they don't belong inside the JSON credential record.
/// </summary>
public sealed record CredentialIcon(
    CredentialIconKind Kind,
    string? SymbolName = null,
    string? BackgroundColorHex = null);
