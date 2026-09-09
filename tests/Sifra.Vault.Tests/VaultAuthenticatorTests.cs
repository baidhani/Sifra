using Sifra.Vault.Auth;

namespace Sifra.Vault.Tests;

public sealed class VaultAuthenticatorTests : IDisposable
{
    private readonly string _dataDirectory;
    private readonly string _vaultLogPath;
    private readonly string _cloudLogPath;

    public VaultAuthenticatorTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-auth-tests-" + Guid.NewGuid());
        _vaultLogPath = Path.Combine(_dataDirectory, "vault-access.log");
        _cloudLogPath = Path.Combine(_dataDirectory, "cloud-access.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private VaultAuthenticator CreateAuthenticator() =>
        new(new VaultAccessCredentialStore(_dataDirectory), new FileAccessAuditLog(_vaultLogPath));

    [Fact]
    public void Authenticate_RegardlessOfCloudSignInState_RequiresItsOwnCredential()
    {
        var vaultAuth = CreateAuthenticator();
        vaultAuth.SetCredential("correct-horse-battery-staple");

        var cloud = new LocalFakeCloudAuthProvider();
        cloud.SignIn("user@example.com");

        // Correct vault credential succeeds while cloud is signed in.
        Assert.True(vaultAuth.Authenticate("correct-horse-battery-staple"));

        cloud.SignOut();

        // Same vault credential still succeeds after cloud sign-out — vault
        // auth never looked at cloud state either time.
        Assert.True(vaultAuth.Authenticate("correct-horse-battery-staple"));
    }

    [Fact]
    public void Authenticate_WithoutAnyCloudAuthenticationHavingEverHappened_StillSucceeds()
    {
        // Failure path: "user attempts to access the vault without cloud
        // authentication." Correct behaviour is that this is not a failure
        // at all — vault access never required cloud auth to begin with.
        var vaultAuth = CreateAuthenticator();
        vaultAuth.SetCredential("correct-horse-battery-staple");

        Assert.True(vaultAuth.Authenticate("correct-horse-battery-staple"));
    }

    [Fact]
    public void Authenticate_WhenCloudAuthenticationFails_VaultAccessIsUnaffected()
    {
        // Failure path: "cloud authentication fails but vault access is needed."
        var vaultAuth = CreateAuthenticator();
        vaultAuth.SetCredential("correct-horse-battery-staple");

        var cloud = new LocalFakeCloudAuthProvider { SimulateFailure = true };
        Assert.Throws<CloudAuthenticationException>(() => cloud.SignIn("user@example.com"));

        Assert.True(vaultAuth.Authenticate("correct-horse-battery-staple"));
    }

    [Fact]
    public void Authenticate_WithWrongCredential_Fails()
    {
        // Failure path: "user forgets vault authentication credentials" —
        // the wrong credential is rejected, not silently accepted.
        var vaultAuth = CreateAuthenticator();
        vaultAuth.SetCredential("correct-horse-battery-staple");

        Assert.False(vaultAuth.Authenticate("wrong-guess"));
    }

    [Fact]
    public void VaultAndCloudAccess_AreLoggedToCompletelySeparateLogs()
    {
        var vaultAuth = CreateAuthenticator();
        vaultAuth.SetCredential("correct-horse-battery-staple");

        var cloudLog = new FileAccessAuditLog(_cloudLogPath);
        cloudLog.Record("cloud_sign_in", success: true);
        cloudLog.Record("cloud_sign_out", success: true);

        vaultAuth.Authenticate("correct-horse-battery-staple");
        vaultAuth.Authenticate("wrong-guess");

        var vaultLog = new FileAccessAuditLog(_vaultLogPath).ReadAll();
        var cloudLogLines = new FileAccessAuditLog(_cloudLogPath).ReadAll();

        Assert.Equal(2, vaultLog.Count);
        Assert.Contains(vaultLog, line => line.Contains("vault_authenticate") && line.Contains("success"));
        Assert.Contains(vaultLog, line => line.Contains("vault_authenticate") && line.Contains("failure"));

        Assert.Equal(2, cloudLogLines.Count);
        Assert.All(cloudLogLines, line => Assert.DoesNotContain("vault_authenticate", line));
        Assert.All(vaultLog, line => Assert.DoesNotContain("cloud_sign_in", line));
    }
}
