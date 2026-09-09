using System.Text.Json;

namespace Sifra.Vault.Auth;

/// <summary>
/// Reads and writes the vault access credential file. Separate file from
/// vault.json (STORY-001) and from anything cloud-related — no code path
/// here ever reads a cloud token, and no cloud code ever reads this file.
/// </summary>
public sealed class VaultAccessCredentialStore
{
    private const string FileName = "vault-access-credential.json";

    private readonly string _filePath;

    public VaultAccessCredentialStore(string? dataDirectory = null)
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

    public bool Exists() => File.Exists(_filePath);

    public VaultAccessCredentialRecord? Load()
    {
        if (!Exists())
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<VaultAccessCredentialRecord>(json);
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not read vault access credential file '{_filePath}'.", ex);
        }
    }

    public void Save(VaultAccessCredentialRecord record)
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
            throw new VaultStorageException($"Could not write vault access credential file '{_filePath}'.", ex);
        }
    }
}
