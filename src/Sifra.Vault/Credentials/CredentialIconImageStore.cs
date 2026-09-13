namespace Sifra.Vault.Credentials;

/// <summary>
/// One image blob per credential (a fetched favicon or a user-supplied
/// custom icon) — overwritten wholesale on every change, unlike
/// AttachmentStore's per-attachment list. Stored as plaintext bytes on
/// disk: an icon is a decorative UI preference, not a secret, so it does
/// not need the vault's AES-GCM path.
/// </summary>
public sealed class CredentialIconImageStore
{
    private readonly string _directory;

    public CredentialIconImageStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra");
        _directory = Path.Combine(directory, "icons");

        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not create icon storage directory '{_directory}'.", ex);
        }
    }

    public byte[]? Read(string credentialId)
    {
        var path = PathFor(credentialId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not read icon file '{path}'.", ex);
        }
    }

    public void Save(string credentialId, byte[] bytes)
    {
        var path = PathFor(credentialId);
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not write icon file '{path}'.", ex);
        }
    }

    public void Delete(string credentialId)
    {
        var path = PathFor(credentialId);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not delete icon file '{path}'.", ex);
        }
    }

    private string PathFor(string credentialId) => Path.Combine(_directory, credentialId + ".img");
}
