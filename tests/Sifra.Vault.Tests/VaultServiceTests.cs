using Sifra.Vault;

namespace Sifra.Vault.Tests;

public sealed class VaultServiceTests : IDisposable
{
    private readonly string _dataDirectory;

    public VaultServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-vault-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateVault_WhenNoVaultExists_ReturnsRecoveryKeyAndPersistsOnlyItsHash()
    {
        var store = new VaultStore(_dataDirectory);
        var service = new VaultService(store);

        var recoveryKey = service.CreateVault();

        Assert.False(string.IsNullOrWhiteSpace(recoveryKey));
        Assert.True(store.Exists());

        // Trust: the plaintext recovery key must never be persisted anywhere.
        // vault.db is a binary SQLite file (Phase 3) — Latin1 maps every
        // byte to one char without throwing, so an embedded plaintext
        // substring still shows up the same way for this check.
        var rawFileContents = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(_dataDirectory, "vault.db")));
        Assert.DoesNotContain(recoveryKey, rawFileContents, StringComparison.Ordinal);

        var record = store.Load();
        Assert.NotNull(record);
        Assert.False(string.IsNullOrWhiteSpace(record!.RecoveryKeyHashBase64));
        Assert.False(string.IsNullOrWhiteSpace(record.RecoveryKeySaltBase64));
    }

    [Fact]
    public void CreateVault_WhenVaultAlreadyExists_ThrowsSoCallerCanPromptToUseExistingVault()
    {
        var store = new VaultStore(_dataDirectory);
        var service = new VaultService(store);
        service.CreateVault();

        Assert.Throws<VaultAlreadyExistsException>(() => service.CreateVault());
    }

    [Fact]
    public void CreateVault_WhenStorageDirectoryCannotBeCreated_ThrowsVaultStorageException()
    {
        // A file sitting where the storage directory needs to be created
        // makes Directory.CreateDirectory fail — simulates a storage error.
        var blockingFilePath = Path.Combine(Path.GetTempPath(), "sifra-vault-tests-blocked-" + Guid.NewGuid());
        File.WriteAllText(blockingFilePath, "not a directory");

        try
        {
            Assert.Throws<VaultStorageException>(() => new VaultStore(blockingFilePath));
        }
        finally
        {
            File.Delete(blockingFilePath);
        }
    }
}
