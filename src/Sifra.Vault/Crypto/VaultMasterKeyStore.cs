using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Crypto;

/// <summary>
/// Persists the named key slots (slot id -> wrapped VMK), keyed by slot id
/// (e.g. "master-password", "recovery-key"). SQLite-backed (Phase 3),
/// migrated from a flat JSON dictionary file — see CredentialStore's remarks.
/// </summary>
public sealed class VaultMasterKeyStore
{
    private const string LegacyFileName = "vault-master-key.json";

    private readonly string? _dataDirectory;

    public VaultMasterKeyStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
        MigrateFromLegacyJsonIfNeeded(connection, _dataDirectory);
    }

    public Dictionary<string, VaultMasterKeySlot> LoadAll()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadAllInternal(connection);
    }

    public bool HasAnySlot() => LoadAll().Count > 0;

    public VaultMasterKeySlot? LoadSlot(string slotId) =>
        LoadAll().TryGetValue(slotId, out var slot) ? slot : null;

    public void SaveSlot(string slotId, VaultMasterKeySlot slot)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        SaveSlotInternal(connection, slotId, slot);
    }

    /// <summary>
    /// Removes a slot outright — used to roll back a failed "join existing
    /// vault" attempt (wrong password) so the device is left exactly as it
    /// was before the join attempt, rather than with a dangling slot.
    /// </summary>
    public void DeleteSlot(string slotId)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM vault_master_key_slots WHERE slot_id = $id";
        cmd.Parameters.AddWithValue("$id", slotId);
        cmd.ExecuteNonQuery();
    }

    private static void SaveSlotInternal(SqliteConnection connection, string slotId, VaultMasterKeySlot slot)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO vault_master_key_slots (slot_id, kek_salt, wrapped_key) VALUES ($id, $salt, $wrapped)
                ON CONFLICT(slot_id) DO UPDATE SET kek_salt = excluded.kek_salt, wrapped_key = excluded.wrapped_key
                """;
            cmd.Parameters.AddWithValue("$id", slotId);
            cmd.Parameters.AddWithValue("$salt", slot.KekSaltBase64);
            cmd.Parameters.AddWithValue("$wrapped", slot.WrappedKeyBase64);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the vault master key database.", ex);
        }
    }

    private static Dictionary<string, VaultMasterKeySlot> LoadAllInternal(SqliteConnection connection)
    {
        var result = new Dictionary<string, VaultMasterKeySlot>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT slot_id, kek_salt, wrapped_key FROM vault_master_key_slots ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = new VaultMasterKeySlot(reader.GetString(1), reader.GetString(2));
        }
        return result;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS vault_master_key_slots (
                slot_id TEXT PRIMARY KEY,
                kek_salt TEXT NOT NULL,
                wrapped_key TEXT NOT NULL
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
            countCmd.CommandText = "SELECT COUNT(*) FROM vault_master_key_slots";
            var count = (long)countCmd.ExecuteScalar()!;
            if (count > 0)
            {
                return;
            }
        }

        Dictionary<string, VaultMasterKeySlot>? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<Dictionary<string, VaultMasterKeySlot>>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy vault master key file '{legacyPath}' for migration.", ex);
        }

        if (legacy is not null)
        {
            foreach (var (slotId, slot) in legacy)
            {
                SaveSlotInternal(connection, slotId, slot);
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
