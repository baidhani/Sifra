using System.Text.Json;

namespace Sifra.Vault.Sync;

/// <summary>
/// Local, file-backed store for vault data items. Every read and write here
/// is pure disk I/O — no network call sits anywhere on this path, which is
/// what makes "view and edit" work identically online or offline.
/// </summary>
public sealed class VaultDataStore
{
    private const string FileName = "vault-data.json";

    private readonly string _filePath;

    public VaultDataStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sifra");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not create vault storage directory '{directory}'.", ex);
        }

        _filePath = Path.Combine(directory, FileName);
    }

    public IReadOnlyList<VaultDataItem> GetAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<VaultDataItem>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<VaultDataItem>>(json) ?? new List<VaultDataItem>();
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not read vault data file '{_filePath}'.", ex);
        }
    }

    /// <summary>Insert-or-replace by Id. Calling this twice with the same item is a no-op the second time.</summary>
    public void Upsert(VaultDataItem item)
    {
        var items = GetAll().Where(existing => existing.Id != item.Id).Append(item).ToList();
        Save(items);
    }

    private void Save(List<VaultDataItem> items)
    {
        var json = JsonSerializer.Serialize(items);
        var tempFilePath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not write vault data file '{_filePath}'.", ex);
        }
    }
}
