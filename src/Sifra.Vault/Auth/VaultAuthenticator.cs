using System.Security.Cryptography;
using Sifra.Vault.Audit;

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
    private readonly AuditLogger? _trustSpineLogger;

    /// <param name="auditLog">The STORY-013 access log, kept separate from cloud access logging.</param>
    /// <param name="trustSpineLogger">
    /// Optional (STORY-015). When provided, every completed Authenticate
    /// call is also logged to the trust-spine audit trail with a unique
    /// operation id. Null means no trust-spine logging — existing callers
    /// and tests are unaffected. Never logs the credential itself.
    /// </param>
    public VaultAuthenticator(VaultAccessCredentialStore store, IAccessAuditLog auditLog, AuditLogger? trustSpineLogger = null)
    {
        _store = store;
        _auditLog = auditLog;
        _trustSpineLogger = trustSpineLogger;
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
            _trustSpineLogger?.Log(nameof(Authenticate), Environment.UserName, details: "success=false (no credential set)");
            return false;
        }

        var salt = Convert.FromBase64String(record.SaltBase64);
        var expectedHash = Convert.FromBase64String(record.HashBase64);
        var actualHash = Hash(credential, salt);

        var success = CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        _auditLog.Record("vault_authenticate", success);
        _trustSpineLogger?.Log(nameof(Authenticate), Environment.UserName, details: $"success={success}");
        return success;
    }

    private static byte[] Hash(string credential, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(credential, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashLengthBytes);
}
