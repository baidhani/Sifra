using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Session;

/// <summary>
/// Links the recovery key to the vault master key at setup time, and
/// performs recovery later. Recovery genuinely restores access to
/// existing credentials (REQ-009) rather than just resetting the login
/// check — the recovery key unwraps the SAME vault master key the
/// forgotten master password was wrapping, via a separate key slot
/// (VaultEncryptionService.RecoveryKeySlot).
/// </summary>
public sealed class VaultRecoveryService
{
    private readonly VaultService _vaultService;
    private readonly VaultAuthenticator _authenticator;
    private readonly VaultEncryptionService _encryption;
    private readonly AuditLogger? _auditLogger;

    public VaultRecoveryService(
        VaultService vaultService,
        VaultAuthenticator authenticator,
        VaultEncryptionService encryption,
        AuditLogger? auditLogger = null)
    {
        _vaultService = vaultService;
        _authenticator = authenticator;
        _encryption = encryption;
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// Completes vault setup: call this once, immediately after
    /// VaultService.CreateVault() returns the recovery key — that is the
    /// only moment its plaintext exists. Wraps the vault master key under
    /// both the initial master password and the recovery key, and sets
    /// the auth verifier.
    /// </summary>
    public void EstablishRecoverySlot(string recoveryKeyPlaintext, string initialMasterPassword)
    {
        var vmk = _encryption.DeriveKey(initialMasterPassword, VaultEncryptionService.MasterPasswordSlot);
        _encryption.AddSlot(VaultEncryptionService.RecoveryKeySlot, recoveryKeyPlaintext, vmk);
        _authenticator.SetCredential(initialMasterPassword);
    }

    /// <exception cref="InvalidRecoveryKeyException">The recovery key does not match this vault.</exception>
    /// <exception cref="RecoveryKeyExpiredException">The recovery key was already used.</exception>
    /// <exception cref="WeakMasterPasswordException">The new password is too short.</exception>
    public void Recover(string recoveryKey, string newMasterPassword)
    {
        if (!_vaultService.VerifyRecoveryKey(recoveryKey))
        {
            _auditLogger?.Log(nameof(Recover), Environment.UserName, details: "outcome=invalid_key");
            throw new InvalidRecoveryKeyException();
        }

        if (_vaultService.IsRecoveryKeyConsumed())
        {
            _auditLogger?.Log(nameof(Recover), Environment.UserName, details: "outcome=expired_key");
            throw new RecoveryKeyExpiredException();
        }

        if (string.IsNullOrEmpty(newMasterPassword) || newMasterPassword.Length < MasterPasswordService.MinLength)
        {
            throw new WeakMasterPasswordException($"New password must be at least {MasterPasswordService.MinLength} characters.");
        }

        var vmk = _encryption.DeriveKey(recoveryKey, VaultEncryptionService.RecoveryKeySlot);
        _encryption.AddSlot(VaultEncryptionService.MasterPasswordSlot, newMasterPassword, vmk);

        _authenticator.SetCredential(newMasterPassword);
        _vaultService.ConsumeRecoveryKey();

        // Never log the recovery key or new password — only that recovery succeeded.
        _auditLogger?.Log(nameof(Recover), Environment.UserName, details: "outcome=success");
    }
}
