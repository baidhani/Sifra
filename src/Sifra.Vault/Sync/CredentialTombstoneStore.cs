using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Sync;

/// <summary>
/// SQLite-backed store of deletion tombstones (Phase 3 sync). New in
/// Phase 3, so unlike the other stores there is no legacy JSON file to
/// migrate from.
/// </summary>
public sealed class CredentialTombstoneStore
{
    private readonly string? _dataDirectory;

    public CredentialTombstoneStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
    }

    /// <summary>Records (or re-records, if already deleted once) that a credential was deleted at this moment.</summary>
    public void Add(string id, DateTimeOffset deletedAtUtc)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO credential_tombstones (id, deleted_at) VALUES ($id, $deletedAt)
                ON CONFLICT(id) DO UPDATE SET deleted_at = excluded.deleted_at
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$deletedAt", deletedAtUtc.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the credential tombstone database.", ex);
        }
    }

    public CredentialTombstone? Load(string id)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, deleted_at FROM credential_tombstones WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new CredentialTombstone(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1))) : null;
    }

    public IReadOnlyList<CredentialTombstone> LoadAll()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        var result = new List<CredentialTombstone>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, deleted_at FROM credential_tombstones ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CredentialTombstone(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1))));
        }
        return result;
    }

    /// <summary>
    /// Deletes tombstones older than the given age threshold — otherwise
    /// the table grows forever. Low-stakes to purge slightly too early or
    /// late for a personal vault (see the sync design discussion): the
    /// worst case of purging too early is a very-long-offline device
    /// resurrecting an already-deleted credential, which the user can
    /// simply delete again.
    /// </summary>
    public void PurgeOlderThan(DateTimeOffset thresholdUtc)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM credential_tombstones WHERE deleted_at < $threshold";
            cmd.Parameters.AddWithValue("$threshold", thresholdUtc.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not purge the credential tombstone database.", ex);
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS credential_tombstones (
                id TEXT PRIMARY KEY,
                deleted_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
