using System.Text.Json;

namespace Sifra.Vault.Credentials;

/// <summary>
/// Local, file-backed store of password history records (ciphertext at
/// rest, same atomic-write pattern as CredentialStore).
/// </summary>
public sealed class PasswordHistoryStore
{
    private const string FileName = "password_history.json";

    private readonly string _filePath;

    public PasswordHistoryStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not create password history storage directory '{directory}'.", ex);
        }

        _filePath = Path.Combine(directory, FileName);
    }

    public IReadOnlyList<PasswordHistoryRecord> GetAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<PasswordHistoryRecord>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<PasswordHistoryRecord>>(json) ?? new List<PasswordHistoryRecord>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not read password history file '{_filePath}'.", ex);
        }
    }

    public void Save(IReadOnlyList<PasswordHistoryRecord> records)
    {
        var json = JsonSerializer.Serialize(records);
        var tempFilePath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not write password history file '{_filePath}'.", ex);
        }
    }
}
