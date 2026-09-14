using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Attachments;

/// <summary>
/// SQLite-backed store of attachment metadata (plaintext) — migrated from
/// a flat JSON file (Phase 3), see CredentialStore's remarks — plus the
/// actual encrypted file bytes, still one file per attachment under a
/// dedicated subdirectory. Blob storage is untouched by this migration: a
/// vault with a few large attachments still shouldn't bloat vault.db the
/// way it wouldn't have bloated credentials.json before.
/// </summary>
public sealed class AttachmentStore
{
    private const string LegacyMetadataFileName = "attachments.json";

    private readonly string? _dataDirectory;
    private readonly string _blobDirectory;

    public AttachmentStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;
        var directory = VaultDatabase.ResolveDataDirectory(dataDirectory);
        _blobDirectory = Path.Combine(directory, "attachments");

        try
        {
            Directory.CreateDirectory(_blobDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not create attachment storage directory '{_blobDirectory}'.", ex);
        }

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
        MigrateFromLegacyJsonIfNeeded(connection, directory);
    }

    public IReadOnlyList<CredentialAttachment> GetAllForCredential(string credentialId)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, credential_id, file_name, kind, size_bytes, created_at FROM attachments WHERE credential_id = $cid ORDER BY rowid";
        cmd.Parameters.AddWithValue("$cid", credentialId);
        return ReadAttachments(cmd);
    }

    public CredentialAttachment? FindById(string id)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, credential_id, file_name, kind, size_bytes, created_at FROM attachments WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAttachments(cmd).FirstOrDefault();
    }

    public void Add(CredentialAttachment metadata, byte[] encryptedBytes)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO attachments (id, credential_id, file_name, kind, size_bytes, created_at) VALUES ($id, $cid, $name, $kind, $size, $created)";
            cmd.Parameters.AddWithValue("$id", metadata.Id);
            cmd.Parameters.AddWithValue("$cid", metadata.CredentialId);
            cmd.Parameters.AddWithValue("$name", metadata.FileName);
            cmd.Parameters.AddWithValue("$kind", metadata.Kind.ToString());
            cmd.Parameters.AddWithValue("$size", metadata.SizeBytes);
            cmd.Parameters.AddWithValue("$created", metadata.CreatedAtUtc.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the attachments database.", ex);
        }

        WriteBlob(metadata.Id, encryptedBytes);
    }

    public byte[] ReadEncryptedBlob(string attachmentId)
    {
        var path = BlobPath(attachmentId);
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not read attachment blob '{path}'.", ex);
        }
    }

    public void Delete(string attachmentId)
    {
        using (var connection = VaultDatabase.OpenConnection(_dataDirectory))
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM attachments WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", attachmentId);
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                throw new VaultStorageException("Could not delete from the attachments database.", ex);
            }
        }

        var path = BlobPath(attachmentId);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not delete attachment blob '{path}'.", ex);
        }
    }

    /// <summary>Called when a credential itself is deleted, so its attachments don't become orphaned files.</summary>
    public void DeleteAllForCredential(string credentialId)
    {
        foreach (var attachment in GetAllForCredential(credentialId))
        {
            Delete(attachment.Id);
        }
    }

    private static List<CredentialAttachment> ReadAttachments(SqliteCommand cmd)
    {
        var result = new List<CredentialAttachment>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CredentialAttachment(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                Enum.Parse<AttachmentKind>(reader.GetString(3)), reader.GetInt64(4), DateTimeOffset.Parse(reader.GetString(5))));
        }
        return result;
    }

    private void WriteBlob(string attachmentId, byte[] encryptedBytes)
    {
        var path = BlobPath(attachmentId);
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllBytes(tempPath, encryptedBytes);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not write attachment blob '{path}'.", ex);
        }
    }

    private string BlobPath(string attachmentId) => Path.Combine(_blobDirectory, attachmentId + ".enc");

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS attachments (
                id TEXT PRIMARY KEY,
                credential_id TEXT NOT NULL,
                file_name TEXT NOT NULL,
                kind TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_attachments_cid ON attachments(credential_id);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void MigrateFromLegacyJsonIfNeeded(SqliteConnection connection, string directory)
    {
        var legacyPath = Path.Combine(directory, LegacyMetadataFileName);

        if (!File.Exists(legacyPath))
        {
            return;
        }

        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM attachments";
            var count = (long)countCmd.ExecuteScalar()!;
            if (count > 0)
            {
                return;
            }
        }

        List<CredentialAttachment>? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            legacy = JsonSerializer.Deserialize<List<CredentialAttachment>>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new VaultStorageException($"Could not read legacy attachments metadata file '{legacyPath}' for migration.", ex);
        }

        if (legacy is not null && legacy.Count > 0)
        {
            using var transaction = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO attachments (id, credential_id, file_name, kind, size_bytes, created_at) VALUES ($id, $cid, $name, $kind, $size, $created)";
            var pId = cmd.Parameters.Add("$id", SqliteType.Text);
            var pCid = cmd.Parameters.Add("$cid", SqliteType.Text);
            var pName = cmd.Parameters.Add("$name", SqliteType.Text);
            var pKind = cmd.Parameters.Add("$kind", SqliteType.Text);
            var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
            var pCreated = cmd.Parameters.Add("$created", SqliteType.Text);

            foreach (var item in legacy)
            {
                pId.Value = item.Id;
                pCid.Value = item.CredentialId;
                pName.Value = item.FileName;
                pKind.Value = item.Kind.ToString();
                pSize.Value = item.SizeBytes;
                pCreated.Value = item.CreatedAtUtc.ToString("o");
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
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
