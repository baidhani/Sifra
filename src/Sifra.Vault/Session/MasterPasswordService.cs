using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Session;

/// <summary>
/// Changes the master password. Verifies the current password, then
/// re-wraps the vault master key under the new password
/// (VaultEncryptionService.RewrapMasterKey) instead of re-encrypting
/// every credential — REQ-008. Reuses VaultAuthenticator.SetCredential
/// (STORY-013, unmodified) to update the auth verifier to match.
///
/// Important: any VaultSession already unlocked with the OLD password
/// becomes stale after a successful change — its remembered credential
/// can no longer unwrap the vault master key. Callers should call
/// session.Lock() immediately after a successful change and require the
/// user to unlock again with the new password. This service does not
/// reach into VaultSession itself to enforce that (VaultSession is a
/// prior story's file); it fails loudly and clearly if a stale session
/// credential is used afterward, rather than silently.
/// </summary>
public sealed class MasterPasswordService
{
    public const int MinLength = 8;

    private readonly VaultAuthenticator _authenticator;
    private readonly VaultEncryptionService _encryption;
    private readonly AuditLogger? _auditLogger;

    public MasterPasswordService(VaultAuthenticator authenticator, VaultEncryptionService encryption, AuditLogger? auditLogger = null)
    {
        _authenticator = authenticator;
        _encryption = encryption;
        _auditLogger = auditLogger;
    }

    /// <exception cref="IncorrectMasterPasswordException">The current password is wrong.</exception>
    /// <exception cref="WeakMasterPasswordException">The new password is too short.</exception>
    public void ChangePassword(string currentPassword, string newPassword)
    {
        if (!_authenticator.Authenticate(currentPassword))
        {
            throw new IncorrectMasterPasswordException();
        }

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinLength)
        {
            throw new WeakMasterPasswordException($"New password must be at least {MinLength} characters.");
        }

        _encryption.RewrapMasterKey(currentPassword, newPassword);
        _authenticator.SetCredential(newPassword);

        // Never log either password value — only that the change happened.
        _auditLogger?.Log(nameof(ChangePassword), Environment.UserName);
    }
}
