using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Sharing;

/// <summary>
/// Persists shares by id. SQLite-backed (Phase 3), migrated from a flat
/// JSON dictionary file — see CredentialStore's remarks.
/// </summary>
public sealed class ShareRegistryStore
{
    private const string LegacyFileName = "share-registry.json";

    private readonly string? _dataDirectory;

    public ShareRegistryStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
        MigrateFromLegacyJsonIfNeeded(connection, _dataDirectory);
    }

    public Dictionary<string, ShareRecord> LoadAll()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadAllInternal(connection);
    }

    public ShareRecord? Load(string shareId) =>
        LoadAll().TryGetValue(shareId, out var record) ? record : null;

    public void Save(ShareRecord record)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        SaveInternal(connection, record);
    }

    private static void SaveInternal(SqliteConnection connection, ShareRecord record)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO shares (id, recipient_label, label, url, kek_salt, encrypted_username, encrypted_password, created_at, revoked, revoked_at)
                VALUES ($id, $recipient, $label, $url, $salt, $user, $pass, $created, $revoked, $revokedAt)
                ON CONFLICT(id) DO UPDATE SET
                    recipient_label=excluded.recipient_label, label=excluded.label, url=excluded.url,
                    kek_salt=excluded.kek_salt, encrypted_username=excluded.encrypted_username, encrypted_password=excluded.encrypted_password,
                    created_at=excluded.created_at, revoked=excluded.revoked, revoked_at=excluded.revoked_at
                """;
            cmd.Parameters.AddWithValue("$id", record.Id);
            cmd.Parameters.AddWithValue("$recipient", record.RecipientLabel);
            cmd.Parameters.AddWithValue("$label", record.Label);
            cmd.Parameters.AddWithValue("$url", (object?)record.Url ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$salt", record.KekSaltBase64);
            cmd.Parameters.AddWithValue("$user", record.EncryptedUsernameBase64);
            cmd.Parameters.AddWithValue("$pass", record.EncryptedPasswordBase64);
            cmd.Parameters.AddWithValue("$created", record.CreatedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$revoked", record.Revoked ? 1 : 0);
            cmd.Parameters.AddWithValue("$revokedAt", (object?)record.RevokedAtUtc?.ToString("o") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the share registry database.", ex);
        }
    }

    private static Dictionary<string, ShareRecord> LoadAllInternal(SqliteConnection connection)
    {
        var result = new Dictionary<string, ShareRecord>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, recipient_label, label, url, kek_salt, encrypted_username, encrypted_password, created_at, revoked, revoked_at FROM shares ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            result[id] = new ShareRecord(
                id, reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7)),
                reader.GetInt32(8) == 1,
                reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)));
        }
        return result;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS shares (
                id TEXT PRIMARY KEY,
                recipient_label TEXT NOT NULL,
                label TEXT NOT NULL,
                url TEXT,
                kek_salt TEXT NOT NULL,
                encrypted_username TEXT NOT NULL,
                encrypted_password TEXT NOT NULL,
                created_at TEXT NOT NULL,
                revoked INTEGER NOT NULL,
                revoked_at TEXT
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

        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM shares";
            var count = (long)countCmd.ExecuteScalar()!;
            if (count > 0)
            {
                return;
            }
        }

        Dictionary<string, ShareRecord>? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<Dictionary<string, ShareRecord>>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy share registry file '{legacyPath}' for migration.", ex);
        }

        if (legacy is not null)
        {
            foreach (var record in legacy.Values)
            {
                SaveInternal(connection, record);
            }
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
