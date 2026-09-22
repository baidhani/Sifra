using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Sync;

/// <summary>
/// SQLite-backed store of attachment deletion tombstones (Phase 3 sync).
/// Same shape and purpose as CredentialTombstoneStore — see its remarks —
/// kept as its own small store rather than a shared generic one, matching
/// this codebase's convention of one focused store per concept.
/// </summary>
public sealed class AttachmentTombstoneStore
{
    private readonly string? _dataDirectory;

    public AttachmentTombstoneStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
    }

    public void Add(string id, DateTimeOffset deletedAtUtc)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO attachment_tombstones (id, deleted_at) VALUES ($id, $deletedAt)
                ON CONFLICT(id) DO UPDATE SET deleted_at = excluded.deleted_at
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$deletedAt", deletedAtUtc.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the attachment tombstone database.", ex);
        }
    }

    public AttachmentTombstone? Load(string id)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, deleted_at FROM attachment_tombstones WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new AttachmentTombstone(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1))) : null;
    }

    public IReadOnlyList<AttachmentTombstone> LoadAll()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        var result = new List<AttachmentTombstone>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, deleted_at FROM attachment_tombstones ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new AttachmentTombstone(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1))));
        }
        return result;
    }

    /// <summary>Deletes tombstones older than the given age threshold — see CredentialTombstoneStore.PurgeOlderThan's remarks.</summary>
    public void PurgeOlderThan(DateTimeOffset thresholdUtc)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM attachment_tombstones WHERE deleted_at < $threshold";
            cmd.Parameters.AddWithValue("$threshold", thresholdUtc.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not purge the attachment tombstone database.", ex);
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS attachment_tombstones (
                id TEXT PRIMARY KEY,
                deleted_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
