using Sifra.Vault.Auth;

namespace Sifra.Vault.Session;

/// <summary>
/// In-memory lock/unlock state for one run of the application. A fresh
/// session always starts Locked — that is structural (a new process has no
/// unlocked session yet), not a persisted flag, since "unlocked" describes
/// runtime state that should never be written to disk. Reuses
/// VaultAuthenticator (STORY-013) unmodified for the actual password check.
///
/// Known hardening gap: the credential is held as a plain string for the
/// unlocked session's lifetime, because CredentialService (STORY-002)
/// takes the credential per call as a string. .NET strings cannot be
/// reliably zeroed from memory. Moving to derived key material held only
/// as bytes would require changing CredentialService's API — left for a
/// future hardening pass rather than done silently or done by editing a
/// prior, already-verified story's file for this one.
/// </summary>
public sealed class VaultSession
{
    private readonly VaultAuthenticator _authenticator;
    private string? _unlockedCredential;

    public VaultSession(VaultAuthenticator authenticator)
    {
        _authenticator = authenticator;
    }

    public VaultLockState State { get; private set; } = VaultLockState.Locked;

    /// <returns>
    /// True if now unlocked (including if it already was). False if the
    /// credential was incorrect — the vault stays Locked, this is an
    /// expected outcome (REQ-006: rejecting incorrect passwords), not an
    /// exception.
    /// </returns>
    /// <exception cref="VaultStorageException">
    /// The underlying credential store could not be read — surfaces
    /// unchanged; the session is left Locked since it never got the chance
    /// to transition (models "crashes during unlock" leaving no partial state).
    /// </exception>
    public bool Unlock(string credential)
    {
        if (State == VaultLockState.Unlocked)
        {
            return true; // idempotent — unlocking an already-unlocked vault is a no-op success
        }

        if (!_authenticator.Authenticate(credential))
        {
            return false;
        }

        State = VaultLockState.Unlocked;
        _unlockedCredential = credential;
        return true;
    }

    public void Lock()
    {
        State = VaultLockState.Locked;
        _unlockedCredential = null;
    }

    /// <exception cref="VaultLockedException">The vault is currently locked.</exception>
    public string RequireUnlockedCredential() =>
        State == VaultLockState.Unlocked
            ? _unlockedCredential!
            : throw new VaultLockedException();
}
