using System.Security.Cryptography;
using System.Text.Json;

namespace Sifra.Vault.Crypto;

/// <summary>
/// Persists the encryption salt exactly once. Generating a new salt on
/// every call would silently change the derived key and make everything
/// already encrypted permanently undecryptable — EnsureSalt() is
/// idempotent specifically to prevent that.
/// </summary>
public sealed class VaultEncryptionKeyStore
{
    private const string FileName = "vault-encryption-salt.json";
    private const int SaltLengthBytes = 16;

    private readonly string _filePath;
    private readonly object _lock = new();

    public VaultEncryptionKeyStore(string? dataDirectory = null)
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

    /// <returns>The existing salt if one was already persisted, otherwise a newly generated one that is now persisted.</returns>
    public byte[] EnsureSalt()
    {
        lock (_lock)
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var record = JsonSerializer.Deserialize<VaultEncryptionSaltRecord>(json)!;
                return Convert.FromBase64String(record.SaltBase64);
            }

            var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
            Save(new VaultEncryptionSaltRecord(Convert.ToBase64String(salt)));
            return salt;
        }
    }

    private void Save(VaultEncryptionSaltRecord record)
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
            throw new VaultStorageException($"Could not write vault encryption salt file '{_filePath}'.", ex);
        }
    }
}
