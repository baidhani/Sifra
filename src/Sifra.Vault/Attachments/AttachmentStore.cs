using System.Text.Json;
using Sifra.Vault;

namespace Sifra.Vault.Attachments;

/// <summary>
/// Local, file-backed store of attachment metadata (plaintext, same
/// atomic-write pattern as CredentialStore) plus the actual encrypted file
/// bytes, one file per attachment under a dedicated subdirectory — kept
/// separate from credentials.json so a vault with a few large attachments
/// doesn't bloat every read/write of the credential list itself.
/// </summary>
public sealed class AttachmentStore
{
    private const string MetadataFileName = "attachments.json";

    private readonly string _metadataFilePath;
    private readonly string _blobDirectory;

    public AttachmentStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sifra");
        _blobDirectory = Path.Combine(directory, "attachments");

        try
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(_blobDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not create attachment storage directory '{_blobDirectory}'.", ex);
        }

        _metadataFilePath = Path.Combine(directory, MetadataFileName);
    }

    public IReadOnlyList<CredentialAttachment> GetAllForCredential(string credentialId) =>
        GetAllMetadata().Where(a => a.CredentialId == credentialId).ToList();

    public CredentialAttachment? FindById(string id) =>
        GetAllMetadata().FirstOrDefault(a => a.Id == id);

    public void Add(CredentialAttachment metadata, byte[] encryptedBytes)
    {
        var items = GetAllMetadata().Append(metadata).ToList();
        SaveMetadata(items);
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
        var items = GetAllMetadata().Where(a => a.Id != attachmentId).ToList();
        SaveMetadata(items);

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

    private IReadOnlyList<CredentialAttachment> GetAllMetadata()
    {
        if (!File.Exists(_metadataFilePath))
        {
            return Array.Empty<CredentialAttachment>();
        }

        try
        {
            var json = File.ReadAllText(_metadataFilePath);
            return JsonSerializer.Deserialize<List<CredentialAttachment>>(json) ?? new List<CredentialAttachment>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not read attachments metadata file '{_metadataFilePath}'.", ex);
        }
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

    private void SaveMetadata(List<CredentialAttachment> items)
    {
        var json = JsonSerializer.Serialize(items);
        var tempFilePath = _metadataFilePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _metadataFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not write attachments metadata file '{_metadataFilePath}'.", ex);
        }
    }
}
