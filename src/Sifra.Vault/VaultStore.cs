using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault;

/// <summary>
/// Reads and writes the single vault record for this device. SQLite-backed
/// (Phase 3), migrated from a flat JSON file — see CredentialStore's
/// remarks. Exists() checks for the row, not the underlying vault.db file:
/// that file can legitimately already exist (another store's constructor
/// may have created it first) before any vault has actually been set up.
/// </summary>
public sealed class VaultStore
{
    private const string LegacyFileName = "vault.json";

    private readonly string? _dataDirectory;

    /// <param name="dataDirectory">
    /// Overrides where the vault database lives. Defaults to the current
    /// user's application-data folder. Tests should always pass an
    /// isolated temp directory here so runs never touch a real user's data.
    /// </param>
    public VaultStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
        MigrateFromLegacyJsonIfNeeded(connection, _dataDirectory);
    }

    public bool Exists()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadInternal(connection) is not null;
    }

    public VaultRecord? Load()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadInternal(connection);
    }

    /// <summary>Writes the vault record. Atomicity comes from SQLite's own transactional writes now, not a temp-file swap.</summary>
    public void Save(VaultRecord record)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        SaveInternal(connection, record);
    }

    private static VaultRecord? LoadInternal(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT json_value FROM vault_record WHERE id = 1";
        var json = cmd.ExecuteScalar() as string;
        return json is null ? null : JsonSerializer.Deserialize<VaultRecord>(json);
    }

    private static void SaveInternal(SqliteConnection connection, VaultRecord record)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO vault_record (id, json_value) VALUES (1, $json) ON CONFLICT(id) DO UPDATE SET json_value = excluded.json_value";
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(record));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the vault database.", ex);
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS vault_record (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                json_value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void MigrateFromLegacyJsonIfNeeded(SqliteConnection connection, string? dataDirectory)
    {
        var directory = VaultDatabase.ResolveDataDirectory(dataDirectory);
        var legacyPath = Path.Combine(directory, LegacyFileName);

        if (!File.Exists(legacyPath))
        {
            return;
        }

        if (LoadInternal(connection) is not null)
        {
            return;
        }

        VaultRecord? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<VaultRecord>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy vault file '{legacyPath}' for migration.", ex);
        }

        if (legacy is not null)
        {
            SaveInternal(connection, legacy);
        }

        try
        {
            File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Data is already migrated — failing to rename the old file is a cleanup nicety, not a failure.
        }
    }
}
