using System.Text.Json;

namespace Sifra.Vault.Credentials;

/// <summary>
/// Local, file-backed store of credentials (ciphertext at rest). Same
/// atomic-write pattern as every other store in this codebase.
/// </summary>
public sealed class CredentialStore
{
    private const string FileName = "credentials.json";

    private readonly string _filePath;

    public CredentialStore(string? dataDirectory = null)
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

    public IReadOnlyList<Credential> GetAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<Credential>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<Credential>>(json) ?? new List<Credential>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not read credentials file '{_filePath}'.", ex);
        }
    }

    /// <summary>Insert-or-replace by Id.</summary>
    public void Upsert(Credential credential)
    {
        var items = GetAll().Where(existing => existing.Id != credential.Id).Append(credential).ToList();
        Save(items);
    }

    public void Delete(string id)
    {
        var items = GetAll().Where(existing => existing.Id != id).ToList();
        Save(items);
    }

    private void Save(List<Credential> items)
    {
        var json = JsonSerializer.Serialize(items);
        var tempFilePath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not write credentials file '{_filePath}'.", ex);
        }
    }
}
