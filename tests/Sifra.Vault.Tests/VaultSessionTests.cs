using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Session;

namespace Sifra.Vault.Tests;

public sealed class VaultSessionTests : IDisposable
{
    private const string MasterPassword = "correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public VaultSessionTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-session-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private VaultAuthenticator CreateAuthenticator() =>
        new(new VaultAccessCredentialStore(_dataDirectory), new FileAccessAuditLog(Path.Combine(_dataDirectory, "vault-access.log")));

    private CredentialService CreateCredentialService() => new(
        new CredentialStore(_dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
        new FakeCredentialClipboard());

    [Fact]
    public void NewSession_StartsLocked()
    {
        // Acceptance: reopening the application leaves the vault locked.
        // A brand-new VaultSession models exactly that — no unlock has happened yet.
        var session = new VaultSession(CreateAuthenticator());

        Assert.Equal(VaultLockState.Locked, session.State);
    }

    [Fact]
    public void RequireUnlockedCredential_WhileLocked_Throws()
    {
        var session = new VaultSession(CreateAuthenticator());

        Assert.Throws<VaultLockedException>(() => session.RequireUnlockedCredential());
    }

    [Fact]
    public void Unlock_WithCorrectCredential_MakesAllCredentialsAccessible()
    {
        // Acceptance: unlocking makes all credentials accessible.
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(MasterPassword);
        var credentialService = CreateCredentialService();
        credentialService.Add(MasterPassword, "GitHub", "firas", "hunter2", "https://github.com");

        var session = new VaultSession(authenticator);
        var unlocked = session.Unlock(MasterPassword);

        Assert.True(unlocked);
        Assert.Equal(VaultLockState.Unlocked, session.State);

        var list = credentialService.List(session.RequireUnlockedCredential());
        Assert.Single(list);
        Assert.Equal("firas", list[0].Username);
    }

    [Fact]
    public void Unlock_CalledTwiceWithCorrectCredential_IsIdempotent()
    {
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(MasterPassword);
        var session = new VaultSession(authenticator);

        Assert.True(session.Unlock(MasterPassword));
        Assert.True(session.Unlock(MasterPassword)); // no error on a second unlock
        Assert.Equal(VaultLockState.Unlocked, session.State);
    }

    [Fact]
    public void Lock_ClearsAccessAndRequiresUnlockingAgain()
    {
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(MasterPassword);
        var session = new VaultSession(authenticator);
        session.Unlock(MasterPassword);

        session.Lock();

        Assert.Equal(VaultLockState.Locked, session.State);
        Assert.Throws<VaultLockedException>(() => session.RequireUnlockedCredential());
    }

    [Fact]
    public void Unlock_WithWrongCredential_StaysLockedAndReturnsFalse()
    {
        // Failure path: "user forgets the master password" — rejected, not
        // an exception, matching REQ-006 ("rejecting incorrect passwords").
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(MasterPassword);
        var session = new VaultSession(authenticator);

        var unlocked = session.Unlock("wrong-guess");

        Assert.False(unlocked);
        Assert.Equal(VaultLockState.Locked, session.State);
        Assert.Throws<VaultLockedException>(() => session.RequireUnlockedCredential());
    }

    [Fact]
    public void ReopeningTheApplication_StartsLockedButPersistedCredentialsSurviveIntact()
    {
        // Acceptance (combined): close/reopen -> locked, and data persists
        // encrypted without loss. Simulated by building a completely fresh
        // set of components pointed at the same directory — nothing in
        // memory is shared with the "first run."
        var firstRunAuthenticator = CreateAuthenticator();
        firstRunAuthenticator.SetCredential(MasterPassword);
        CreateCredentialService().Add(MasterPassword, "GitHub", "firas", "hunter2", "https://github.com");

        // "Reopen" — brand-new instances, same directory.
        var secondRunSession = new VaultSession(CreateAuthenticator());
        Assert.Equal(VaultLockState.Locked, secondRunSession.State);

        Assert.True(secondRunSession.Unlock(MasterPassword));
        var list = CreateCredentialService().List(secondRunSession.RequireUnlockedCredential());

        Assert.Single(list);
        Assert.Equal("firas", list[0].Username);
    }

    [Fact]
    public void GetById_WhenStoredCiphertextIsCorrupted_ThrowsInsteadOfReturningGarbage()
    {
        // Failure path: "data corruption occurs during encryption" — a
        // tampered ciphertext value fails AES-GCM's authentication tag
        // check (VaultDecryptionFailedException, from STORY-002), rather
        // than silently decrypting to garbage or crashing unhandled.
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(MasterPassword);
        var credentialService = CreateCredentialService();
        var id = credentialService.Add(MasterPassword, "GitHub", "firas", "hunter2", "https://github.com");

        // Flip one character inside the encrypted password value
        // deterministically — a blind global string replace is flaky
        // (the random ciphertext may not happen to contain that
        // character on a given run) and could corrupt JSON structure
        // instead of the ciphertext.
        var filePath = Path.Combine(_dataDirectory, "credentials.json");
        var original = File.ReadAllText(filePath);
        const string marker = "\"EncryptedPasswordBase64\":\"";
        var valueStart = original.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var flipChar = original[valueStart] == 'A' ? 'B' : 'A';
        var corrupted = original[..valueStart] + flipChar + original[(valueStart + 1)..];
        File.WriteAllText(filePath, corrupted);

        var session = new VaultSession(authenticator);
        session.Unlock(MasterPassword);

        Assert.Throws<VaultDecryptionFailedException>(() => credentialService.GetById(session.RequireUnlockedCredential(), id));
    }

    [Fact]
    public void Unlock_WhenTheUnderlyingStoreFails_PropagatesAndNeverTransitionsToUnlocked()
    {
        // Failure path: "application crashes during vault unlock" — modeled
        // as an exception interrupting the read Unlock() depends on. The
        // session must not have transitioned to Unlocked, and nothing was
        // written to disk (Unlock only reads), so a real crash here could
        // never corrupt persisted data or leave a half-unlocked state.
        var authenticator = CreateAuthenticator();
        authenticator.SetCredential(MasterPassword);
        var session = new VaultSession(authenticator);

        var vaultAccessFilePath = Path.Combine(_dataDirectory, "vault-access-credential.json");
        var originalContents = File.ReadAllText(vaultAccessFilePath);
        File.WriteAllText(vaultAccessFilePath, "{ not valid json");

        try
        {
            Assert.ThrowsAny<Exception>(() => session.Unlock(MasterPassword));
            Assert.Equal(VaultLockState.Locked, session.State);
            Assert.Throws<VaultLockedException>(() => session.RequireUnlockedCredential());
        }
        finally
        {
            File.WriteAllText(vaultAccessFilePath, originalContents);
        }
    }
}
