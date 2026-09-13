namespace Sifra.Vault.Credentials;

/// <summary>
/// The four ways a credential's icon can be set — matching the "Use
/// website icon / Select symbol / Select color / Use custom icon" context
/// menu. Composes CredentialService (for the Icon metadata on the record)
/// with the image blob store and the favicon fetcher, so callers don't
/// need to juggle three collaborators themselves.
/// </summary>
public sealed class CredentialIconService
{
    private readonly CredentialService _credentials;
    private readonly CredentialIconImageStore _images;
    private readonly IFaviconFetcher _faviconFetcher;

    public CredentialIconService(CredentialService credentials, CredentialIconImageStore images, IFaviconFetcher faviconFetcher)
    {
        _credentials = credentials;
        _images = images;
        _faviconFetcher = faviconFetcher;
    }

    public void SetSymbol(string credentialId, string symbolName, string backgroundColorHex)
    {
        _credentials.SetIcon(credentialId, new CredentialIcon(CredentialIconKind.Symbol, SymbolName: symbolName, BackgroundColorHex: backgroundColorHex));
        _images.Delete(credentialId);
    }

    public void SetColor(string credentialId, string backgroundColorHex)
    {
        _credentials.SetIcon(credentialId, new CredentialIcon(CredentialIconKind.InitialLetter, BackgroundColorHex: backgroundColorHex));
        _images.Delete(credentialId);
    }

    public void SetCustom(string credentialId, byte[] imageBytes)
    {
        _images.Save(credentialId, imageBytes);
        _credentials.SetIcon(credentialId, new CredentialIcon(CredentialIconKind.Custom));
    }

    /// <summary>Fetches and stores the website's favicon. Returns false (icon left unchanged) if the site has no reachable favicon.</summary>
    public async Task<bool> SetFromWebsiteAsync(string credentialId, string websiteUrl, CancellationToken cancellationToken)
    {
        var bytes = await _faviconFetcher.FetchAsync(websiteUrl, cancellationToken);
        if (bytes is null)
        {
            return false;
        }

        _images.Save(credentialId, bytes);
        _credentials.SetIcon(credentialId, new CredentialIcon(CredentialIconKind.WebsiteFavicon));
        return true;
    }

    public byte[]? GetImage(string credentialId) => _images.Read(credentialId);

    public void DeleteAllForCredential(string credentialId) => _images.Delete(credentialId);
}
