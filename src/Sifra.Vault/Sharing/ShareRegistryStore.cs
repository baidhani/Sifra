using System.Text.Json;
using Sifra.Vault;

namespace Sifra.Vault.Sharing;

/// <summary>
/// Persists shares by id. Same atomic-write pattern as every other store
/// in this codebase (write to a temp file, then rename over the real one).
/// </summary>
public sealed class ShareRegistryStore
{
    private const string FileName = "share-registry.json";

    private readonly string _filePath;

    public ShareRegistryStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sifra");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not create vault storage directory '{directory}'.", ex);
        }

        _filePath = Path.Combine(directory, FileName);
    }

    public Dictionary<string, ShareRecord> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, ShareRecord>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, ShareRecord>>(json) ?? new Dictionary<string, ShareRecord>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not read share registry file '{_filePath}'.", ex);
        }
    }

    public ShareRecord? Load(string shareId) =>
        LoadAll().TryGetValue(shareId, out var record) ? record : null;

    public void Save(ShareRecord record)
    {
        var all = LoadAll();
        all[record.Id] = record;

        var json = JsonSerializer.Serialize(all);
        var tempFilePath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not write share registry file '{_filePath}'.", ex);
        }
    }
}
