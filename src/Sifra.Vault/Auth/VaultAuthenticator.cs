using System.Security.Cryptography;

namespace Sifra.Vault.Auth;

/// <summary>
/// Vault-side authentication. Has no reference to ICloudAuthProvider or any
/// cloud state at all — that absence is what proves REQ-012 structurally,
/// not just by test assertion. Vault access always requires its own
/// credential, regardless of whether cloud sign-in ever happened.
/// </summary>
public sealed class VaultAuthenticator
{
    private const int SaltLengthBytes = 16;
    private const int HashLengthBytes = 32;
    private const int Pbkdf2Iterations = 210_000; // OWASP current minimum for PBKDF2-SHA256

    private readonly VaultAccessCredentialStore _store;
    private readonly IAccessAuditLog _auditLog;

    public VaultAuthenticator(VaultAccessCredentialStore store, IAccessAuditLog auditLog)
    {
        _store = store;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Establishes the vault access credential. This story only needs a
    /// credential to exist so authentication is testable — the real setup
    /// UX (tied to master-password creation) belongs to STORY-004/005.
    /// </summary>
    public void SetCredential(string credential)
    {
        if (string.IsNullOrEmpty(credential))
        {
            throw new ArgumentException("Credential must not be empty.", nameof(credential));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var hash = Hash(credential, salt);
        _store.Save(new VaultAccessCredentialRecord(Convert.ToBase64String(salt), Convert.ToBase64String(hash)));
    }

    /// <summary>
    /// Authenticates against the vault's own credential. Never consults any
    /// cloud provider or cloud auth state.
    /// </summary>
    public bool Authenticate(string credential)
    {
        var record = _store.Load();
        if (record is null)
        {
            _auditLog.Record("vault_authenticate", success: false);
            return false;
        }

        var salt = Convert.FromBase64String(record.SaltBase64);
        var expectedHash = Convert.FromBase64String(record.HashBase64);
        var actualHash = Hash(credential, salt);

        var success = CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        _auditLog.Record("vault_authenticate", success);
        return success;
    }

    private static byte[] Hash(string credential, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(credential, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashLengthBytes);
}
