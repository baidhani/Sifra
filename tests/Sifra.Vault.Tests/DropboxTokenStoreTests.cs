using Sifra.Vault.Dropbox;

namespace Sifra.Vault.Tests;

public sealed class DropboxTokenStoreTests : IDisposable
{
    private readonly string _dataDirectory;

    public DropboxTokenStoreTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-dropbox-token-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void LoadRefreshToken_WhenNoneSaved_ReturnsNull()
    {
        Assert.Null(new DropboxTokenStore(_dataDirectory).LoadRefreshToken());
    }

    [Fact]
    public void SaveRefreshToken_ThenLoad_RoundTripsTheSameValue()
    {
        var store = new DropboxTokenStore(_dataDirectory);

        store.SaveRefreshToken("a-real-looking-refresh-token-value");

        Assert.Equal("a-real-looking-refresh-token-value", store.LoadRefreshToken());
    }

    [Fact]
    public void SaveRefreshToken_PersistsAcrossNewStoreInstances()
    {
        new DropboxTokenStore(_dataDirectory).SaveRefreshToken("persisted-token");

        var reopened = new DropboxTokenStore(_dataDirectory);

        Assert.Equal("persisted-token", reopened.LoadRefreshToken());
    }

    [Fact]
    public void SaveRefreshToken_IsEncryptedAtRest_NeverStoredAsPlaintext()
    {
        var store = new DropboxTokenStore(_dataDirectory);
        const string secret = "super-secret-refresh-token-should-not-appear-in-clear";

        store.SaveRefreshToken(secret);

        var rawFileContents = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(_dataDirectory, "dropbox-refresh-token.dat")));
        Assert.DoesNotContain(secret, rawFileContents, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_RemovesTheStoredToken()
    {
        var store = new DropboxTokenStore(_dataDirectory);
        store.SaveRefreshToken("some-token");

        store.Clear();

        Assert.Null(store.LoadRefreshToken());
    }

    [Fact]
    public void Clear_WhenNothingWasEverSaved_DoesNotThrow()
    {
        new DropboxTokenStore(_dataDirectory).Clear();
        Assert.True(true);
    }

    [Fact]
    public void SaveRefreshToken_CalledTwice_OverwritesTheOldValue()
    {
        var store = new DropboxTokenStore(_dataDirectory);
        store.SaveRefreshToken("old-token");

        store.SaveRefreshToken("new-token");

        Assert.Equal("new-token", store.LoadRefreshToken());
    }
}
