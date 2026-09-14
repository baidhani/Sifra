using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Credentials;

/// <summary>
/// SQLite-backed store of password history records (ciphertext at rest).
/// Migrated from a flat JSON file (Phase 3) — see CredentialStore's remarks.
/// Save() keeps its "replace everything" semantics (matches every
/// existing caller: load all, mutate the in-memory list, Save it back).
/// </summary>
public sealed class PasswordHistoryStore
{
    private const string LegacyFileName = "password_history.json";

    private readonly string? _dataDirectory;

    public PasswordHistoryStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
        MigrateFromLegacyJsonIfNeeded(connection, _dataDirectory);
    }

    public IReadOnlyList<PasswordHistoryRecord> GetAll()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadAll(connection);
    }

    public void Save(IReadOnlyList<PasswordHistoryRecord> records)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        SaveInternal(connection, records);
    }

    private static void SaveInternal(SqliteConnection connection, IReadOnlyList<PasswordHistoryRecord> records)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM password_history";
                deleteCmd.ExecuteNonQuery();
            }

            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = "INSERT INTO password_history (credential_id, field_name, encrypted_value, changed_at) VALUES ($cid, $field, $value, $changed)";
            var pCid = insertCmd.Parameters.Add("$cid", SqliteType.Text);
            var pField = insertCmd.Parameters.Add("$field", SqliteType.Text);
            var pValue = insertCmd.Parameters.Add("$value", SqliteType.Text);
            var pChanged = insertCmd.Parameters.Add("$changed", SqliteType.Text);

            foreach (var record in records)
            {
                pCid.Value = record.CredentialId;
                pField.Value = record.FieldName;
                pValue.Value = record.EncryptedValueBase64;
                pChanged.Value = record.ChangedAtUtc.ToString("o");
                insertCmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the password history database.", ex);
        }
    }

    private static List<PasswordHistoryRecord> LoadAll(SqliteConnection connection)
    {
        var result = new List<PasswordHistoryRecord>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT credential_id, field_name, encrypted_value, changed_at FROM password_history ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new PasswordHistoryRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3))));
        }
        return result;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS password_history (
                credential_id TEXT NOT NULL,
                field_name TEXT NOT NULL,
                encrypted_value TEXT NOT NULL,
                changed_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_password_history_cid_field ON password_history(credential_id, field_name);
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

        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM password_history";
            var count = (long)countCmd.ExecuteScalar()!;
            if (count > 0)
            {
                return;
            }
        }

        List<PasswordHistoryRecord>? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<List<PasswordHistoryRecord>>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy password history file '{legacyPath}' for migration.", ex);
        }

        if (legacy is not null && legacy.Count > 0)
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
