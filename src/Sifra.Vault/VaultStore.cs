using System.Text.Json;

namespace Sifra.Vault;

/// <summary>
/// Reads and writes the single vault file for this device. Local-first,
/// file-based storage — no network or cloud dependency.
/// </summary>
public sealed class VaultStore
{
    private const string VaultFileName = "vault.json";

    private readonly string _vaultFilePath;

    /// <param name="dataDirectory">
    /// Overrides where the vault file lives. Defaults to the current user's
    /// application-data folder. Tests should always pass an isolated temp
    /// directory here so runs never touch a real user's data.
    /// </param>
    public VaultStore(string? dataDirectory = null)
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
        catch (UnauthorizedAccessException ex)
        {
            throw new VaultStorageException($"Could not create vault storage directory '{directory}'.", ex);
        }

        _vaultFilePath = Path.Combine(directory, VaultFileName);
    }

    public bool Exists() => File.Exists(_vaultFilePath);

    public VaultRecord? Load()
    {
        if (!Exists())
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_vaultFilePath);
            return JsonSerializer.Deserialize<VaultRecord>(json);
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not read vault file '{_vaultFilePath}'.", ex);
        }
    }

    /// <summary>
    /// Writes the vault file atomically: the new content is written to a temp
    /// file first, then swapped into place, so a crash mid-write can never
    /// leave a partially-written vault file behind.
    /// </summary>
    public void Save(VaultRecord record)
    {
        var json = JsonSerializer.Serialize(record);
        var tempFilePath = _vaultFilePath + ".tmp";

        try
        {
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _vaultFilePath, overwrite: true);
        }
        catch (IOException ex)
        {
            throw new VaultStorageException($"Could not write vault file '{_vaultFilePath}'.", ex);
        }
    }
}
