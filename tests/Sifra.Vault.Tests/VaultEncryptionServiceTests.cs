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
        var service = new VaultEncryptionService(new VaultEncryptionKeyStore(_dataDirectory));
        var key = service.DeriveKey("correct-horse-battery-staple");

        var ciphertext = service.Encrypt("hunter2", key);
        var decrypted = service.Decrypt(ciphertext, key);

        Assert.Equal("hunter2", decrypted);
        Assert.DoesNotContain("hunter2", ciphertext);
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsVaultDecryptionFailedException()
    {
        // Maps to "wrong master password" — a real, expected failure mode, not corruption.
        var service = new VaultEncryptionService(new VaultEncryptionKeyStore(_dataDirectory));
        var rightKey = service.DeriveKey("correct-horse-battery-staple");
        var wrongKey = service.DeriveKey("some-other-password");

        var ciphertext = service.Encrypt("hunter2", rightKey);

        Assert.Throws<VaultDecryptionFailedException>(() => service.Decrypt(ciphertext, wrongKey));
    }

    [Fact]
    public void DeriveKey_AcrossSeparateInstancesPointingAtTheSameDirectory_IsStable()
    {
        // Critical correctness property: if the salt were regenerated each
        // time, everything already encrypted would become permanently
        // undecryptable. Two independent instances must derive the same key.
        var service1 = new VaultEncryptionService(new VaultEncryptionKeyStore(_dataDirectory));
        var key1 = service1.DeriveKey("correct-horse-battery-staple");
        var ciphertext = service1.Encrypt("hunter2", key1);

        var service2 = new VaultEncryptionService(new VaultEncryptionKeyStore(_dataDirectory));
        var key2 = service2.DeriveKey("correct-horse-battery-staple");

        Assert.Equal("hunter2", service2.Decrypt(ciphertext, key2));
    }
}
