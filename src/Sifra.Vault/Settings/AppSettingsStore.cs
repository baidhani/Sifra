using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Settings;

/// <summary>
/// SQLite-backed store for app preferences (plaintext — never secret
/// data). Migrated from a flat JSON file (Phase 3). Stored as a single
/// JSON blob in one row rather than individual columns: unlike
/// credentials, there's no per-field query/update benefit here (nothing
/// ever reads or writes just one setting), so a real column-per-field
/// schema would only add migration friction every time a new setting is
/// added, for no actual gain.
/// </summary>
public sealed class AppSettingsStore
{
    private const string LegacyFileName = "settings.json";

    private readonly string? _dataDirectory;

    public AppSettingsStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
        MigrateFromLegacyJsonIfNeeded(connection, _dataDirectory);
    }

    public AppSettings Get()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadInternal(connection) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        SaveInternal(connection, settings);
    }

    private static AppSettings? LoadInternal(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT json_value FROM settings WHERE id = 1";
        var json = cmd.ExecuteScalar() as string;
        return json is null ? null : JsonSerializer.Deserialize<AppSettings>(json);
    }

    private static void SaveInternal(SqliteConnection connection, AppSettings settings)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO settings (id, json_value) VALUES (1, $json) ON CONFLICT(id) DO UPDATE SET json_value = excluded.json_value";
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the settings database.", ex);
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
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

        AppSettings? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<AppSettings>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy settings file '{legacyPath}' for migration.", ex);
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
