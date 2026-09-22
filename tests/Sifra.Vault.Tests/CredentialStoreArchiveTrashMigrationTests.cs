using Microsoft.Data.Sqlite;
using Sifra.Vault.Credentials;

namespace Sifra.Vault.Tests;

/// <summary>
/// Proves an on-disk database created before the Archive/Trash columns
/// existed still opens and works correctly — CredentialStore must add
/// those columns to the existing table rather than assuming a fresh one.
/// </summary>
public sealed class CredentialStoreArchiveTrashMigrationTests : IDisposable
{
    private readonly string _dataDirectory;

    public CredentialStoreArchiveTrashMigrationTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-credential-migration-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void CredentialStore_OpensAnOldSchemaDatabaseWithoutTheNewColumns_AndDefaultsThemFalse()
    {
        // Simulates a pre-existing database from before Archive/Trash
        // shipped: create the "credentials" table with only the old
        // columns, insert one row directly, then open it through
        // CredentialStore exactly as a real upgrade would.
        Directory.CreateDirectory(_dataDirectory);
        var dbPath = Path.Combine(_dataDirectory, "vault.db");
        using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            connection.Open();
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = """
                CREATE TABLE credentials (
                    id TEXT PRIMARY KEY,
                    label TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    is_favorite INTEGER NOT NULL,
                    icon_kind TEXT,
                    icon_symbol_name TEXT,
                    icon_background_color_hex TEXT
                );
                CREATE TABLE credential_fields (
                    credential_id TEXT NOT NULL REFERENCES credentials(id) ON DELETE CASCADE,
                    name TEXT NOT NULL,
                    type TEXT NOT NULL,
                    encrypted_value TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE credential_tags (
                    credential_id TEXT NOT NULL REFERENCES credentials(id) ON DELETE CASCADE,
                    tag TEXT NOT NULL
                );
                """;
            createCmd.ExecuteNonQuery();

            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = "INSERT INTO credentials (id, label, updated_at, created_at, is_favorite) VALUES ('old-1', 'Old Credential', $now, $now, 0)";
            insertCmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
            insertCmd.ExecuteNonQuery();
        }

        // Opening CredentialStore must run the column migration, not throw.
        var store = new CredentialStore(_dataDirectory);
        var all = store.GetAll();

        var record = Assert.Single(all);
        Assert.Equal("old-1", record.Id);
        Assert.False(record.IsArchived);
        Assert.False(record.IsDeleted);
        Assert.Null(record.DeletedAtUtc);

        // And the new columns are genuinely usable afterward, not just tolerated.
        store.Upsert(record with { IsArchived = true });
        Assert.True(store.GetAll().Single().IsArchived);
    }
}
