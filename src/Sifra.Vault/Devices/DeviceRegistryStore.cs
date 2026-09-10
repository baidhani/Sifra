using System.Text.Json;

namespace Sifra.Vault.Devices;

/// <summary>Persists the device registry (device id -> record). Same atomic-write pattern as every other store in this codebase.</summary>
public sealed class DeviceRegistryStore
{
    private const string FileName = "devices.json";

    private readonly string _filePath;

    public DeviceRegistryStore(string? dataDirectory = null)
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

    public Dictionary<string, DeviceRecord> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, DeviceRecord>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, DeviceRecord>>(json) ?? new Dictionary<string, DeviceRecord>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not read device registry file '{_filePath}'.", ex);
        }
    }

    public DeviceRecord? Load(string deviceId) =>
        LoadAll().TryGetValue(deviceId, out var record) ? record : null;

    public void Save(DeviceRecord record)
    {
        var all = LoadAll();
        all[record.DeviceId] = record;

        var json = JsonSerializer.Serialize(all);
        var tempFilePath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not write device registry file '{_filePath}'.", ex);
        }
    }
}
