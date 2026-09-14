using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Devices;

/// <summary>
/// SQLite-backed device registry (device id -> record). Migrated from a
/// flat JSON dictionary file (Phase 3) — see CredentialStore's remarks.
/// </summary>
public sealed class DeviceRegistryStore
{
    private const string LegacyFileName = "devices.json";

    private readonly string? _dataDirectory;

    public DeviceRegistryStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
        MigrateFromLegacyJsonIfNeeded(connection, _dataDirectory);
    }

    public Dictionary<string, DeviceRecord> LoadAll()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadAllInternal(connection);
    }

    public DeviceRecord? Load(string deviceId) =>
        LoadAll().TryGetValue(deviceId, out var record) ? record : null;

    public void Save(DeviceRecord record)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        SaveInternal(connection, record);
    }

    private static void SaveInternal(SqliteConnection connection, DeviceRecord record)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO devices (device_id, device_name, secret_salt, secret_hash, enrolled_at, revoked_at, failed_password_attempts)
                VALUES ($id, $name, $salt, $hash, $enrolled, $revoked, $attempts)
                ON CONFLICT(device_id) DO UPDATE SET
                    device_name=excluded.device_name, secret_salt=excluded.secret_salt, secret_hash=excluded.secret_hash,
                    enrolled_at=excluded.enrolled_at, revoked_at=excluded.revoked_at, failed_password_attempts=excluded.failed_password_attempts
                """;
            cmd.Parameters.AddWithValue("$id", record.DeviceId);
            cmd.Parameters.AddWithValue("$name", record.DeviceName);
            cmd.Parameters.AddWithValue("$salt", record.SecretSaltBase64);
            cmd.Parameters.AddWithValue("$hash", record.SecretHashBase64);
            cmd.Parameters.AddWithValue("$enrolled", record.EnrolledAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$revoked", (object?)record.RevokedAtUtc?.ToString("o") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$attempts", record.FailedPasswordAttempts);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the device registry database.", ex);
        }
    }

    private static Dictionary<string, DeviceRecord> LoadAllInternal(SqliteConnection connection)
    {
        var result = new Dictionary<string, DeviceRecord>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT device_id, device_name, secret_salt, secret_hash, enrolled_at, revoked_at, failed_password_attempts FROM devices ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var deviceId = reader.GetString(0);
            result[deviceId] = new DeviceRecord(
                deviceId, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
                reader.GetInt32(6));
        }
        return result;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS devices (
                device_id TEXT PRIMARY KEY,
                device_name TEXT NOT NULL,
                secret_salt TEXT NOT NULL,
                secret_hash TEXT NOT NULL,
                enrolled_at TEXT NOT NULL,
                revoked_at TEXT,
                failed_password_attempts INTEGER NOT NULL DEFAULT 0
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
            countCmd.CommandText = "SELECT COUNT(*) FROM devices";
            var count = (long)countCmd.ExecuteScalar()!;
            if (count > 0)
            {
                return;
            }
        }

        Dictionary<string, DeviceRecord>? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<Dictionary<string, DeviceRecord>>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy device registry file '{legacyPath}' for migration.", ex);
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
