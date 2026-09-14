using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Auth;

/// <summary>
/// Reads and writes the vault access credential record. SQLite-backed
/// (Phase 3), migrated from a flat JSON file — see CredentialStore's
/// remarks. Separate table from vault_record and from anything
/// cloud-related — no code path here ever reads a cloud token, and no
/// cloud code ever reads this table.
/// </summary>
public sealed class VaultAccessCredentialStore
{
    private const string LegacyFileName = "vault-access-credential.json";

    private readonly string? _dataDirectory;

    public VaultAccessCredentialStore(string? dataDirectory = null)
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

    public VaultAccessCredentialRecord? Load()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadInternal(connection);
    }

    public void Save(VaultAccessCredentialRecord record)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        SaveInternal(connection, record);
    }

    private static VaultAccessCredentialRecord? LoadInternal(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT json_value FROM vault_access_credential WHERE id = 1";
        var json = cmd.ExecuteScalar() as string;
        return json is null ? null : JsonSerializer.Deserialize<VaultAccessCredentialRecord>(json);
    }

    private static void SaveInternal(SqliteConnection connection, VaultAccessCredentialRecord record)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO vault_access_credential (id, json_value) VALUES (1, $json) ON CONFLICT(id) DO UPDATE SET json_value = excluded.json_value";
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(record));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the vault access credential database.", ex);
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS vault_access_credential (
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

        VaultAccessCredentialRecord? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<VaultAccessCredentialRecord>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy vault access credential file '{legacyPath}' for migration.", ex);
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
