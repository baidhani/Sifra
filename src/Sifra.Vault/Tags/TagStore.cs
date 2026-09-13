using System.Text.Json;

namespace Sifra.Vault.Tags;

/// <summary>
/// Local, file-backed store of the shared tag registry (plaintext,
/// same atomic-write pattern as CredentialStore/AttachmentStore).
/// </summary>
public sealed class TagStore
{
    private const string FileName = "tags.json";

    private readonly string _filePath;

    public TagStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not create tag storage directory '{directory}'.", ex);
        }

        _filePath = Path.Combine(directory, FileName);
    }

    public IReadOnlyList<TagDefinition> GetAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<TagDefinition>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<TagDefinition>>(json) ?? new List<TagDefinition>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not read tags file '{_filePath}'.", ex);
        }
    }

    public void Save(IReadOnlyList<TagDefinition> tags)
    {
        var json = JsonSerializer.Serialize(tags);
        var tempFilePath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not write tags file '{_filePath}'.", ex);
        }
    }
}
