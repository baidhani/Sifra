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

    /// <summary>
    /// Heals credentials whose icon metadata claims an image (WebsiteFavicon
    /// or Custom) but whose actual image bytes are missing locally — the
    /// state a device ends up in right after a fresh restore/join, since
    /// CredentialIconImageStore is local-only, plaintext, decorative UI
    /// preference storage and was deliberately never wired into
    /// VaultSyncService/AttachmentBlobSyncService the way credential fields
    /// and attachments are. Re-fetches the favicon when the credential has
    /// a Website field (the one case a missing image can be repaired
    /// automatically); otherwise falls back to the safe default
    /// (InitialLetter, keeping whatever background color was already set)
    /// rather than leaving metadata that permanently claims an image exists
    /// when it never will again (true for Custom, whose original bytes
    /// cannot be recovered from anywhere).
    /// </summary>
    public async Task RepairMissingImagesAsync(string vaultCredential, CancellationToken cancellationToken = default)
    {
        foreach (var view in _credentials.List(vaultCredential))
        {
            if (view.Icon?.Kind is not (CredentialIconKind.WebsiteFavicon or CredentialIconKind.Custom))
            {
                continue;
            }

            if (_images.Read(view.Id) is not null)
            {
                continue; // the image is actually there — nothing to repair
            }

            var website = view.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Website)?.Value;
            var repaired = !string.IsNullOrWhiteSpace(website) && await SetFromWebsiteAsync(view.Id, website, cancellationToken);

            if (!repaired)
            {
                SetColor(view.Id, view.Icon.BackgroundColorHex ?? "#5B8DEF");
            }
        }
    }
}
