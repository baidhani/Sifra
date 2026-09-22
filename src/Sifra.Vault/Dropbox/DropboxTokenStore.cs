using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Sifra.Vault.Storage;

namespace Sifra.Vault.Dropbox;

/// <summary>
/// Persists a Dropbox OAuth refresh token at rest, encrypted with Windows
/// DPAPI (CurrentUser scope) — the same protection Windows Credential
/// Manager entries get, tied to this Windows user profile on this
/// machine. Lets DropboxAuthProvider.TrySilentSignIn() reconnect to
/// Dropbox on a later app launch without opening a browser again, until
/// the user explicitly signs out or revokes access.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DropboxTokenStore
{
    private const string FileName = "dropbox-refresh-token.dat";

    private readonly string _filePath;

    public DropboxTokenStore(string? dataDirectory = null)
    {
        var directory = VaultDatabase.ResolveDataDirectory(dataDirectory);
        _filePath = Path.Combine(directory, FileName);
    }

    public void SaveRefreshToken(string refreshToken)
    {
        var plainBytes = Encoding.UTF8.GetBytes(refreshToken);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, protectedBytes); // directory already created by ResolveDataDirectory in the constructor
    }

    /// <returns>The stored refresh token, or null if none is stored, or it could not be decrypted (e.g. a different Windows user profile).</returns>
    public string? LoadRefreshToken()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_filePath);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
