using System.Text.Json;

namespace Sifra.Vault.Crypto;

/// <summary>Reads and writes the wrapped vault master key file. Same atomic-write pattern as every other store in this codebase.</summary>
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

    public VaultMasterKeyRecord? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<VaultMasterKeyRecord>(json);
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not read vault master key file '{_filePath}'.", ex);
        }
    }

    public void Save(VaultMasterKeyRecord record)
    {
        var json = JsonSerializer.Serialize(record);
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
