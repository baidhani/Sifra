using Sifra.Vault.Crypto;

namespace Sifra.Vault.Tests;

public sealed class VaultEncryptionServiceTests : IDisposable
{
    private readonly string _dataDirectory;

    public VaultEncryptionServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-crypto-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Encrypt_ThenDecrypt_WithCorrectKey_RoundTripsSuccessfully()
    {
        var service = new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory));
        var key = service.DeriveKey("correct-horse-battery-staple");

        var ciphertext = service.Encrypt("hunter2", key);
        var decrypted = service.Decrypt(ciphertext, key);

        Assert.Equal("hunter2", decrypted);
        Assert.DoesNotContain("hunter2", ciphertext);
    }

    [Fact]
    public void DeriveKey_WithWrongCredentialAfterKeyAlreadyExists_ThrowsVaultDecryptionFailedException()
    {
        // Maps to "wrong master password" — a real, expected failure mode.
        // Envelope encryption (STORY-005/REQ-008): the vault master key is
        // wrapped under a KEK derived from the credential, so a wrong
        // credential fails to unwrap it immediately at DeriveKey() — it
        // can no longer silently produce a different-but-usable key the
        // way direct password-derived keys used to (that was the bug
        // REQ-008 exposed: it made "change password" impossible without
        // re-encrypting everything).
        var service = new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory));
        service.DeriveKey("correct-horse-battery-staple"); // creates and wraps the VMK under this credential

        Assert.Throws<VaultDecryptionFailedException>(() => service.DeriveKey("some-other-password"));
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsVaultDecryptionFailedException()
    {
        // A key that is simply wrong for other reasons (not derived via
        // DeriveKey at all) is still rejected by Decrypt() itself.
        var service = new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory));
        var rightKey = service.DeriveKey("correct-horse-battery-staple");
        var wrongKey = new byte[32]; // all zeros — definitely not the VMK

        var ciphertext = service.Encrypt("hunter2", rightKey);

        Assert.Throws<VaultDecryptionFailedException>(() => service.Decrypt(ciphertext, wrongKey));
    }

    [Fact]
    public void DeriveKey_AcrossSeparateInstancesPointingAtTheSameDirectory_IsStable()
    {
        // Critical correctness property: if the salt were regenerated each
        // time, everything already encrypted would become permanently
        // undecryptable. Two independent instances must derive the same key.
        var service1 = new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory));
        var key1 = service1.DeriveKey("correct-horse-battery-staple");
        var ciphertext = service1.Encrypt("hunter2", key1);

        var service2 = new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory));
        var key2 = service2.DeriveKey("correct-horse-battery-staple");

        Assert.Equal("hunter2", service2.Decrypt(ciphertext, key2));
    }
}
