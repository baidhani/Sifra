using System.Text.Json;

namespace Sifra.Vault.Settings;

/// <summary>
/// Local, file-backed store for app preferences (plaintext, same
/// atomic-write pattern as TagStore/AttachmentStore — no encryption since
/// these are never secret data).
/// </summary>
public sealed class AppSettingsStore
{
    private const string FileName = "settings.json";

    private readonly string _filePath;

    public AppSettingsStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not create settings storage directory '{directory}'.", ex);
        }

        _filePath = Path.Combine(directory, FileName);
    }

    public AppSettings Get()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not read settings file '{_filePath}'.", ex);
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings);
        var tempFilePath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not write settings file '{_filePath}'.", ex);
        }
    }
}
