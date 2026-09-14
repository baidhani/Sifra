using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Tags;

/// <summary>
/// SQLite-backed store of the shared tag registry (plaintext). Migrated
/// from a flat JSON file (Phase 3) — see CredentialStore's remarks for why.
/// Save() keeps its original "replace everything" semantics (that's how
/// every existing caller already uses it), just implemented as
/// delete-then-reinsert inside one transaction instead of rewriting a file.
/// </summary>
public sealed class TagStore
{
    private const string LegacyFileName = "tags.json";

    private readonly string? _dataDirectory;

    public TagStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
        MigrateFromLegacyJsonIfNeeded(connection, _dataDirectory);
    }

    public IReadOnlyList<TagDefinition> GetAll()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadAll(connection);
    }

    public void Save(IReadOnlyList<TagDefinition> tags)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        SaveInternal(connection, tags);
    }

    private static void SaveInternal(SqliteConnection connection, IReadOnlyList<TagDefinition> tags)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM tags";
                deleteCmd.ExecuteNonQuery();
            }

            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = "INSERT INTO tags (id, name, color, pinned_to_top) VALUES ($id, $name, $color, $pinned)";
            var pId = insertCmd.Parameters.Add("$id", SqliteType.Text);
            var pName = insertCmd.Parameters.Add("$name", SqliteType.Text);
            var pColor = insertCmd.Parameters.Add("$color", SqliteType.Text);
            var pPinned = insertCmd.Parameters.Add("$pinned", SqliteType.Integer);

            foreach (var tag in tags)
            {
                pId.Value = tag.Id;
                pName.Value = tag.Name;
                pColor.Value = tag.Color;
                pPinned.Value = tag.PinnedToTop ? 1 : 0;
                insertCmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the tags database.", ex);
        }
    }

    private static List<TagDefinition> LoadAll(SqliteConnection connection)
    {
        var result = new List<TagDefinition>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, color, pinned_to_top FROM tags ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TagDefinition(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1));
        }
        return result;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS tags (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                color TEXT NOT NULL,
                pinned_to_top INTEGER NOT NULL
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
            countCmd.CommandText = "SELECT COUNT(*) FROM tags";
            var count = (long)countCmd.ExecuteScalar()!;
            if (count > 0)
            {
                return;
            }
        }

        List<TagDefinition>? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<List<TagDefinition>>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy tags file '{legacyPath}' for migration.", ex);
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
