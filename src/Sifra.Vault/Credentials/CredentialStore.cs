using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Credentials;

/// <summary>
/// SQLite-backed store of credentials (ciphertext at rest). Migrated from
/// the original flat-JSON-array design (Phase 3) specifically so a
/// single-field edit updates one row instead of rewriting every
/// credential in the vault — the JSON design's cost scaled with total
/// vault size on every edit; this doesn't. A credential's own row
/// preserves its rowid (and therefore its position in GetAll(), matching
/// the old "replace in place, never reorder" behavior) across edits;
/// its field/tag rows are simply replaced each Upsert, since their order
/// is already fully determined by the incoming Fields/Tags list every
/// time — nothing meaningful would be preserved by trying to diff them.
/// </summary>
public sealed class CredentialStore
{
    private const string LegacyFileName = "credentials.json";

    private readonly string? _dataDirectory;

    public CredentialStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
        MigrateFromLegacyJsonIfNeeded(connection, _dataDirectory);
    }

    public IReadOnlyList<Credential> GetAll()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        return LoadAll(connection);
    }

    /// <summary>
    /// Insert-or-replace by Id. Replaces in place when the credential
    /// already exists, rather than removing and re-appending — otherwise
    /// every edit would silently reorder the list to put the edited item
    /// last, which is confusing since list order is meant to reflect
    /// creation order, not last-edited order.
    /// </summary>
    public void Upsert(Credential credential)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        UpsertInternal(connection, credential);
    }

    private static void UpsertInternal(SqliteConnection connection, Credential credential)
    {
        using var transaction = connection.BeginTransaction();

        try
        {
            var exists = ScalarBool(connection, transaction, "SELECT 1 FROM credentials WHERE id = $id", ("$id", credential.Id));

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = exists
                    ? "UPDATE credentials SET label=$label, updated_at=$updated, created_at=$created, is_favorite=$fav, icon_kind=$iconKind, icon_symbol_name=$iconSymbol, icon_background_color_hex=$iconColor WHERE id=$id"
                    : "INSERT INTO credentials (id, label, updated_at, created_at, is_favorite, icon_kind, icon_symbol_name, icon_background_color_hex) VALUES ($id, $label, $updated, $created, $fav, $iconKind, $iconSymbol, $iconColor)";
                cmd.Parameters.AddWithValue("$id", credential.Id);
                cmd.Parameters.AddWithValue("$label", credential.Label);
                cmd.Parameters.AddWithValue("$updated", credential.UpdatedAtUtc.ToString("o"));
                cmd.Parameters.AddWithValue("$created", credential.CreatedAtUtc.ToString("o"));
                cmd.Parameters.AddWithValue("$fav", credential.IsFavorite ? 1 : 0);
                cmd.Parameters.AddWithValue("$iconKind", (object?)credential.Icon?.Kind.ToString() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$iconSymbol", (object?)credential.Icon?.SymbolName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$iconColor", (object?)credential.Icon?.BackgroundColorHex ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            ReplaceFields(connection, transaction, credential.Id, credential.Fields);
            ReplaceTags(connection, transaction, credential.Id, credential.Tags);

            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the credentials database.", ex);
        }
    }

    public void Delete(string id)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        try
        {
            using var cmd = connection.CreateCommand();
            // ON DELETE CASCADE (PRAGMA foreign_keys=ON, set by VaultDatabase)
            // removes the credential's field/tag rows automatically.
            cmd.CommandText = "DELETE FROM credentials WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not delete from the credentials database.", ex);
        }
    }

    private static void ReplaceFields(SqliteConnection connection, SqliteTransaction transaction, string credentialId, IReadOnlyList<CustomField> fields)
    {
        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM credential_fields WHERE credential_id = $cid";
            deleteCmd.Parameters.AddWithValue("$cid", credentialId);
            deleteCmd.ExecuteNonQuery();
        }

        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = "INSERT INTO credential_fields (credential_id, name, type, encrypted_value, updated_at) VALUES ($cid, $name, $type, $value, $updated)";
        var pCid = insertCmd.Parameters.Add("$cid", Microsoft.Data.Sqlite.SqliteType.Text);
        var pName = insertCmd.Parameters.Add("$name", Microsoft.Data.Sqlite.SqliteType.Text);
        var pType = insertCmd.Parameters.Add("$type", Microsoft.Data.Sqlite.SqliteType.Text);
        var pValue = insertCmd.Parameters.Add("$value", Microsoft.Data.Sqlite.SqliteType.Text);
        var pUpdated = insertCmd.Parameters.Add("$updated", Microsoft.Data.Sqlite.SqliteType.Text);

        foreach (var field in fields)
        {
            pCid.Value = credentialId;
            pName.Value = field.Name;
            pType.Value = field.Type.ToString();
            pValue.Value = field.EncryptedValueBase64;
            pUpdated.Value = field.UpdatedAtUtc.ToString("o");
            insertCmd.ExecuteNonQuery();
        }
    }

    private static void ReplaceTags(SqliteConnection connection, SqliteTransaction transaction, string credentialId, IReadOnlyList<string>? tags)
    {
        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM credential_tags WHERE credential_id = $cid";
            deleteCmd.Parameters.AddWithValue("$cid", credentialId);
            deleteCmd.ExecuteNonQuery();
        }

        if (tags is null || tags.Count == 0)
        {
            return;
        }

        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = "INSERT INTO credential_tags (credential_id, tag) VALUES ($cid, $tag)";
        var pCid = insertCmd.Parameters.Add("$cid", Microsoft.Data.Sqlite.SqliteType.Text);
        var pTag = insertCmd.Parameters.Add("$tag", Microsoft.Data.Sqlite.SqliteType.Text);

        foreach (var tag in tags)
        {
            pCid.Value = credentialId;
            pTag.Value = tag;
            insertCmd.ExecuteNonQuery();
        }
    }

    private static List<Credential> LoadAll(SqliteConnection connection)
    {
        var credentials = new Dictionary<string, Credential>();
        var order = new List<string>();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, label, updated_at, created_at, is_favorite, icon_kind, icon_symbol_name, icon_background_color_hex FROM credentials ORDER BY rowid";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                CredentialIcon? icon = reader.IsDBNull(5)
                    ? null
                    : new CredentialIcon(
                        Enum.Parse<CredentialIconKind>(reader.GetString(5)),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7));

                credentials[id] = new Credential(
                    id, reader.GetString(1), new List<CustomField>(),
                    DateTimeOffset.Parse(reader.GetString(2)), DateTimeOffset.Parse(reader.GetString(3)),
                    IsFavorite: reader.GetInt32(4) == 1, Tags: new List<string>(), Icon: icon);
                order.Add(id);
            }
        }

        var fieldsByCredential = new Dictionary<string, List<CustomField>>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT credential_id, name, type, encrypted_value, updated_at FROM credential_fields ORDER BY rowid";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var credentialId = reader.GetString(0);
                if (!fieldsByCredential.TryGetValue(credentialId, out var list))
                {
                    list = new List<CustomField>();
                    fieldsByCredential[credentialId] = list;
                }
                list.Add(new CustomField(
                    reader.GetString(1),
                    reader.GetString(3),
                    Enum.Parse<CustomFieldType>(reader.GetString(2)),
                    DateTimeOffset.Parse(reader.GetString(4))));
            }
        }

        var tagsByCredential = new Dictionary<string, List<string>>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT credential_id, tag FROM credential_tags ORDER BY rowid";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var credentialId = reader.GetString(0);
                if (!tagsByCredential.TryGetValue(credentialId, out var list))
                {
                    list = new List<string>();
                    tagsByCredential[credentialId] = list;
                }
                list.Add(reader.GetString(1));
            }
        }

        var result = new List<Credential>(order.Count);
        foreach (var id in order)
        {
            var credential = credentials[id];
            var fields = fieldsByCredential.TryGetValue(id, out var f) ? f : new List<CustomField>();
            var tags = tagsByCredential.TryGetValue(id, out var t) ? t : new List<string>();
            result.Add(credential with { Fields = fields, Tags = tags });
        }

        return result;
    }

    private static bool ScalarBool(SqliteConnection connection, SqliteTransaction transaction, string sql, (string Name, object Value) param)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(param.Name, param.Value);
        return cmd.ExecuteScalar() is not null;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS credentials (
                id TEXT PRIMARY KEY,
                label TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                is_favorite INTEGER NOT NULL,
                icon_kind TEXT,
                icon_symbol_name TEXT,
                icon_background_color_hex TEXT
            );
            CREATE TABLE IF NOT EXISTS credential_fields (
                credential_id TEXT NOT NULL REFERENCES credentials(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                type TEXT NOT NULL,
                encrypted_value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS credential_tags (
                credential_id TEXT NOT NULL REFERENCES credentials(id) ON DELETE CASCADE,
                tag TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_credential_fields_cid ON credential_fields(credential_id);
            CREATE INDEX IF NOT EXISTS idx_credential_tags_cid ON credential_tags(credential_id);
            CREATE INDEX IF NOT EXISTS idx_credential_tags_tag ON credential_tags(tag);
            """;
        cmd.ExecuteNonQuery();
    }

    // One-time import from the pre-Phase-3 JSON file. Runs only when the
    // credentials table is still empty and a legacy file exists — never
    // overwrites data already in the database. The legacy file is renamed
    // (never deleted) so it survives as a plain-sight backup.
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
            countCmd.CommandText = "SELECT COUNT(*) FROM credentials";
            var count = (long)countCmd.ExecuteScalar()!;
            if (count > 0)
            {
                return; // already migrated (or a fresh install that happens to have a stray legacy file) — never overwrite
            }
        }

        List<Credential>? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<List<Credential>>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy credentials file '{legacyPath}' for migration.", ex);
        }

        if (legacy is not null)
        {
            // UpsertInternal directly against the connection already open
            // here — NOT a new CredentialStore(dataDirectory), which would
            // re-run this exact migration check (table still empty, legacy
            // file still present at that point) and recurse forever.
            foreach (var credential in legacy)
            {
                UpsertInternal(connection, credential);
            }
        }

        try
        {
            File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Migration itself already succeeded (data is in the database) —
            // failing to rename the old file is a cleanup nicety, not a
            // reason to make the whole migration look like it failed.
        }
    }
}
