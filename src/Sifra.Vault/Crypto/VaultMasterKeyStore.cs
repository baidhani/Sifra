using System.Text.Json;

namespace Sifra.Vault.Crypto;

/// <summary>
/// Persists the named key slots (slot id -> wrapped VMK), keyed by slot
/// id (e.g. "master-password", "recovery-key"). Same atomic-write pattern
/// as every other store in this codebase.
/// </summary>
public sealed class VaultMasterKeyStore
{
    private const string FileName = "vault-master-key.json";

    private readonly string _filePath;

    public VaultMasterKeyStore(string? dataDirectory = null)
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

    public Dictionary<string, VaultMasterKeySlot> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, VaultMasterKeySlot>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, VaultMasterKeySlot>>(json) ?? new Dictionary<string, VaultMasterKeySlot>();
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not read vault master key file '{_filePath}'.", ex);
        }
    }

    public bool HasAnySlot() => LoadAll().Count > 0;

    public VaultMasterKeySlot? LoadSlot(string slotId) =>
        LoadAll().TryGetValue(slotId, out var slot) ? slot : null;

    public void SaveSlot(string slotId, VaultMasterKeySlot slot)
    {
        var all = LoadAll();
        all[slotId] = slot;
        Save(all);
    }

    private void Save(Dictionary<string, VaultMasterKeySlot> all)
    {
        var json = JsonSerializer.Serialize(all);
        var tempFilePath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not write vault master key file '{_filePath}'.", ex);
        }
    }
}
