using Sifra.Vault.Settings;
using Xunit;

namespace Sifra.Vault.Tests;

public class AppSettingsStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "sifra-settings-tests-" + Guid.NewGuid());

    [Fact]
    public void Get_WithNoSavedFile_ReturnsDefaults()
    {
        var store = new AppSettingsStore(_tempDir);

        var settings = store.Get();

        Assert.Equal(5, settings.AutoLockMinutes);
    }

    [Fact]
    public void SaveThenGet_RoundTripsAutoLockMinutes()
    {
        var store = new AppSettingsStore(_tempDir);

        store.Save(new AppSettings(AutoLockMinutes: 15));

        Assert.Equal(15, store.Get().AutoLockMinutes);
    }

    [Fact]
    public void SaveWithNullAutoLockMinutes_MeansNever()
    {
        var store = new AppSettingsStore(_tempDir);

        store.Save(new AppSettings(AutoLockMinutes: null));

        Assert.Null(store.Get().AutoLockMinutes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
