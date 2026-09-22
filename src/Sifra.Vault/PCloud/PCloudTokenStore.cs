using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sifra.Vault.Storage;

namespace Sifra.Vault.PCloud;

public sealed record PCloudStoredToken(string AccessToken, string ApiHost);

/// <summary>
/// Persists a pCloud access token (plus the region-specific API host it
/// must be used with — see PCloudAuthProvider's remarks) at rest, encrypted
/// with Windows DPAPI. Unlike Dropbox/Google, pCloud's classic OAuth 2.0
/// token does not expire under normal use (no refresh-token concept in
/// their API) — so this stores the access token itself, not a refresh
/// token, and TrySilentSignIn just needs to confirm it hasn't been revoked.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PCloudTokenStore
{
    private const string FileName = "pcloud-token.dat";

    private readonly string _filePath;

    public PCloudTokenStore(string? dataDirectory = null)
    {
        var directory = VaultDatabase.ResolveDataDirectory(dataDirectory);
        _filePath = Path.Combine(directory, FileName);
    }

    public void SaveToken(PCloudStoredToken token)
    {
        var plainBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token));
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, protectedBytes);
    }

    /// <returns>The stored token, or null if none is stored, or it could not be decrypted (e.g. a different Windows user profile).</returns>
    public PCloudStoredToken? LoadToken()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_filePath);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<PCloudStoredToken>(Encoding.UTF8.GetString(plainBytes));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
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
