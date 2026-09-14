using System.Text.Json;
using Sifra.Vault.Credentials;
using Xunit;

namespace Sifra.Vault.Tests;

/// <summary>Phase 3: CredentialStore moved from a flat JSON file to SQLite. These prove the one-time import path.</summary>
public sealed class CredentialStoreMigrationTests : IDisposable
{
    private readonly string _dataDirectory;

    public CredentialStoreMigrationTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-credential-migration-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_dataDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private void SeedLegacyJson(IReadOnlyList<Credential> credentials)
    {
        var json = JsonSerializer.Serialize(credentials);
        File.WriteAllText(Path.Combine(_dataDirectory, "credentials.json"), json);
    }

    [Fact]
    public void Construction_WithLegacyJsonPresentAndDatabaseEmpty_ImportsAllCredentials()
    {
        var legacy = new List<Credential>
        {
            new("id-1", "Bank", new List<CustomField> { new("Login", "cipher1", CustomFieldType.Login, DateTimeOffset.UtcNow) },
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Tags: new List<string> { "finance" }),
            new("id-2", "Email", new List<CustomField>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };
        SeedLegacyJson(legacy);

        var store = new CredentialStore(_dataDirectory);
        var loaded = store.GetAll();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("id-1", loaded[0].Id);
        Assert.Equal("Bank", loaded[0].Label);
        Assert.Equal("cipher1", loaded[0].Fields.Single().EncryptedValueBase64);
        Assert.Equal(new[] { "finance" }, loaded[0].Tags);
        Assert.Equal("id-2", loaded[1].Id);
    }

    [Fact]
    public void Construction_AfterMigration_RenamesLegacyFileRatherThanDeletingIt()
    {
        SeedLegacyJson(new List<Credential> { new("id-1", "Bank", new List<CustomField>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) });

        _ = new CredentialStore(_dataDirectory);

        Assert.False(File.Exists(Path.Combine(_dataDirectory, "credentials.json")));
        Assert.True(File.Exists(Path.Combine(_dataDirectory, "credentials.json.migrated")));
    }

    [Fact]
    public void Construction_WhenDatabaseAlreadyHasData_NeverReimportsLegacyFile()
    {
        // Simulates a legacy file that happens to still exist (e.g. the
        // ".migrated" rename previously failed) but the database already
        // has real data — migration must never run again and duplicate or
        // overwrite it.
        var store = new CredentialStore(_dataDirectory);
        store.Upsert(new Credential("real-id", "Real Credential", new List<CustomField>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // A stray legacy file with DIFFERENT content than what's already in the database.
        SeedLegacyJson(new List<Credential> { new("stray-id", "Should Not Import", new List<CustomField>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) });

        var reopened = new CredentialStore(_dataDirectory);
        var loaded = reopened.GetAll();

        Assert.Single(loaded);
        Assert.Equal("real-id", loaded[0].Id);
    }

    [Fact]
    public void Construction_WithNoLegacyFileAndEmptyDatabase_StartsEmpty()
    {
        var store = new CredentialStore(_dataDirectory);

        Assert.Empty(store.GetAll());
    }
}
