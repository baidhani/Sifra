using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using static Sifra.Vault.Tests.CredentialFieldTestHelpers;

namespace Sifra.Vault.Tests;

public sealed class CredentialIconServiceTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public CredentialIconServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-credential-icon-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private CredentialService CreateCredentialService() => new(
        new CredentialStore(_dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
        new FakeCredentialClipboard());

    private CredentialIconService CreateIconService(CredentialService credentials, IFaviconFetcher? fetcher = null) =>
        new(credentials, new CredentialIconImageStore(_dataDirectory), fetcher ?? new FakeFaviconFetcher(null));

    [Fact]
    public void SetSymbol_ThenGetById_ReturnsTheSymbolIcon()
    {
        var credentials = CreateCredentialService();
        var icons = CreateIconService(credentials);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", null));

        icons.SetSymbol(id, "Bank24", "#5B8DEF");

        var view = credentials.GetById(VaultCredential, id);
        Assert.Equal(CredentialIconKind.Symbol, view.Icon!.Kind);
        Assert.Equal("Bank24", view.Icon.SymbolName);
        Assert.Equal("#5B8DEF", view.Icon.BackgroundColorHex);
    }

    [Fact]
    public void SetColor_ThenGetById_ReturnsInitialLetterKindWithThatColor()
    {
        var credentials = CreateCredentialService();
        var icons = CreateIconService(credentials);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", null));

        icons.SetColor(id, "#E74C3C");

        var view = credentials.GetById(VaultCredential, id);
        Assert.Equal(CredentialIconKind.InitialLetter, view.Icon!.Kind);
        Assert.Equal("#E74C3C", view.Icon.BackgroundColorHex);
    }

    [Fact]
    public void SetCustom_StoresTheImageBytesAndSetsCustomKind()
    {
        var credentials = CreateCredentialService();
        var icons = CreateIconService(credentials);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", null));
        var bytes = new byte[] { 1, 2, 3, 4 };

        icons.SetCustom(id, bytes);

        var view = credentials.GetById(VaultCredential, id);
        Assert.Equal(CredentialIconKind.Custom, view.Icon!.Kind);
        Assert.Equal(bytes, icons.GetImage(id));
    }

    [Fact]
    public async Task SetFromWebsiteAsync_WhenFaviconFound_StoresItAndReturnsTrue()
    {
        var credentials = CreateCredentialService();
        var favicon = new byte[] { 9, 9, 9 };
        var icons = CreateIconService(credentials, new FakeFaviconFetcher(favicon));
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", "https://bank.example.com"));

        var result = await icons.SetFromWebsiteAsync(id, "https://bank.example.com", CancellationToken.None);

        Assert.True(result);
        var view = credentials.GetById(VaultCredential, id);
        Assert.Equal(CredentialIconKind.WebsiteFavicon, view.Icon!.Kind);
        Assert.Equal(favicon, icons.GetImage(id));
    }

    [Fact]
    public async Task SetFromWebsiteAsync_WhenNoFaviconFound_ReturnsFalseAndLeavesIconUnchanged()
    {
        var credentials = CreateCredentialService();
        var icons = CreateIconService(credentials, new FakeFaviconFetcher(null));
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", "https://bank.example.com"));

        var result = await icons.SetFromWebsiteAsync(id, "https://bank.example.com", CancellationToken.None);

        Assert.False(result);
        var view = credentials.GetById(VaultCredential, id);
        Assert.Null(view.Icon);
    }

    [Fact]
    public void SetSymbol_AfterCustomIcon_RemovesTheOldImageBlob()
    {
        // Switching icon kind must not leave an orphaned image file behind.
        var credentials = CreateCredentialService();
        var icons = CreateIconService(credentials);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", null));
        icons.SetCustom(id, new byte[] { 1, 2, 3 });

        icons.SetSymbol(id, "Bank24", "#5B8DEF");

        Assert.Null(icons.GetImage(id));
    }

    [Fact]
    public void Edit_UnrelatedFieldChange_PreservesTheExistingIcon()
    {
        // Regression: CredentialService.Edit rebuilds the whole Credential
        // record — it must carry the existing Icon forward, otherwise every
        // unrelated edit (renaming, changing the password, ...) would
        // silently reset the icon back to the default initial letter.
        var credentials = CreateCredentialService();
        var icons = CreateIconService(credentials);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", null));
        icons.SetSymbol(id, "Bank24", "#5B8DEF");

        credentials.Edit(VaultCredential, id, "Bank Renamed", LoginFields("user", "pw2", null));

        var view = credentials.GetById(VaultCredential, id);
        Assert.Equal(CredentialIconKind.Symbol, view.Icon!.Kind);
        Assert.Equal("Bank24", view.Icon.SymbolName);
    }

    [Fact]
    public async Task RepairMissingImagesAsync_WebsiteFaviconMissingLocally_RefetchesFromTheWebsiteField()
    {
        // Simulates the state right after a fresh restore: the credential's
        // Icon.Kind metadata (synced) says WebsiteFavicon, but the actual
        // image bytes (never synced — CredentialIconImageStore is local-only)
        // aren't on this device.
        var credentials = CreateCredentialService();
        var favicon = new byte[] { 9, 9, 9 };
        var icons = CreateIconService(credentials, new FakeFaviconFetcher(favicon));
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", "https://bank.example.com"));
        credentials.SetIcon(id, new CredentialIcon(CredentialIconKind.WebsiteFavicon));
        Assert.Null(icons.GetImage(id)); // confirms the "missing locally" precondition

        await icons.RepairMissingImagesAsync(VaultCredential);

        Assert.Equal(favicon, icons.GetImage(id));
        Assert.Equal(CredentialIconKind.WebsiteFavicon, credentials.GetById(VaultCredential, id).Icon!.Kind);
    }

    [Fact]
    public async Task RepairMissingImagesAsync_CustomIconMissingLocallyWithNoWebsite_FallsBackToInitialLetter()
    {
        // A Custom icon's original bytes can never be re-fetched from
        // anywhere — the only safe repair is the default look, not leaving
        // metadata that permanently claims an image exists.
        var credentials = CreateCredentialService();
        var icons = CreateIconService(credentials);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", null));
        credentials.SetIcon(id, new CredentialIcon(CredentialIconKind.Custom, BackgroundColorHex: "#E74C3C"));

        await icons.RepairMissingImagesAsync(VaultCredential);

        var view = credentials.GetById(VaultCredential, id);
        Assert.Equal(CredentialIconKind.InitialLetter, view.Icon!.Kind);
        Assert.Equal("#E74C3C", view.Icon.BackgroundColorHex);
        Assert.Null(icons.GetImage(id));
    }

    [Fact]
    public async Task RepairMissingImagesAsync_WebsiteFaviconMissingLocallyAndUnreachable_FallsBackToInitialLetter()
    {
        var credentials = CreateCredentialService();
        var icons = CreateIconService(credentials, new FakeFaviconFetcher(null)); // site has no reachable favicon
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", "https://bank.example.com"));
        credentials.SetIcon(id, new CredentialIcon(CredentialIconKind.WebsiteFavicon));

        await icons.RepairMissingImagesAsync(VaultCredential);

        Assert.Equal(CredentialIconKind.InitialLetter, credentials.GetById(VaultCredential, id).Icon!.Kind);
    }

    [Fact]
    public async Task RepairMissingImagesAsync_ImageAlreadyPresent_LeavesItUntouched()
    {
        var credentials = CreateCredentialService();
        var icons = CreateIconService(credentials);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "pw", null));
        icons.SetCustom(id, new byte[] { 1, 2, 3 });

        await icons.RepairMissingImagesAsync(VaultCredential);

        Assert.Equal(CredentialIconKind.Custom, credentials.GetById(VaultCredential, id).Icon!.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, icons.GetImage(id));
    }

    private sealed class FakeFaviconFetcher : IFaviconFetcher
    {
        private readonly byte[]? _result;
        public FakeFaviconFetcher(byte[]? result) => _result = result;
        public Task<byte[]?> FetchAsync(string websiteUrl, CancellationToken cancellationToken) => Task.FromResult(_result);
    }
}
