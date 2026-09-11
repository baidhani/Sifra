using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Health;

namespace Sifra.Vault.Tests;

public sealed class PasswordHealthServiceTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public PasswordHealthServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-health-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private CredentialService CreateCredentialService() => new(
        new CredentialStore(_dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
        new FakeCredentialClipboard());

    [Fact]
    public void AnalyzeAll_WithAStrongPassword_IsNotFlaggedWeak()
    {
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "GitHub", "firas", "Tr0ub4dor&3-Zebra!", "https://github.com");
        var service = new PasswordHealthService(credentials);

        var reports = service.AnalyzeAll(VaultCredential);

        Assert.Single(reports);
        Assert.False(reports[0].IsWeak);
        Assert.Empty(reports[0].Reasons);
    }

    [Fact]
    public void AnalyzeAll_WithATooShortPassword_FlagsTooShort()
    {
        // Acceptance: weak passwords are identified.
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "Site", "user", "Ab1!", null);
        var service = new PasswordHealthService(credentials);

        var report = service.AnalyzeAll(VaultCredential).Single();

        Assert.True(report.IsWeak);
        Assert.Contains(PasswordWeaknessReason.TooShort, report.Reasons);
    }

    [Fact]
    public void AnalyzeAll_WithALowVarietyPassword_FlagsLowCharacterVariety()
    {
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "Site", "user", "aaaaaaaaaaaaaaaa", null);
        var service = new PasswordHealthService(credentials);

        var report = service.AnalyzeAll(VaultCredential).Single();

        Assert.True(report.IsWeak);
        Assert.Contains(PasswordWeaknessReason.LowCharacterVariety, report.Reasons);
    }

    [Fact]
    public void AnalyzeAll_WithAKnownCommonPassword_FlagsCommonPassword()
    {
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "Site", "user", "password123", null);
        var service = new PasswordHealthService(credentials);

        var report = service.AnalyzeAll(VaultCredential).Single();

        Assert.True(report.IsWeak);
        Assert.Contains(PasswordWeaknessReason.CommonPassword, report.Reasons);
    }

    [Fact]
    public void AnalyzeAll_WithTwoCredentialsSharingTheSamePassword_FlagsBothAsReused()
    {
        var credentials = CreateCredentialService();
        var id1 = credentials.Add(VaultCredential, "Site A", "userA", "Tr0ub4dor&3-Zebra!", null);
        var id2 = credentials.Add(VaultCredential, "Site B", "userB", "Tr0ub4dor&3-Zebra!", null);
        var service = new PasswordHealthService(credentials);

        var reports = service.AnalyzeAll(VaultCredential);

        Assert.True(reports.Single(r => r.CredentialId == id1).IsReused);
        Assert.True(reports.Single(r => r.CredentialId == id2).IsReused);
    }

    [Fact]
    public void AnalyzeAll_WithAllUniquePasswords_FlagsNoneAsReused()
    {
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "Site A", "userA", "Tr0ub4dor&3-Zebra!", null);
        credentials.Add(VaultCredential, "Site B", "userB", "Different-Passw0rd!9", null);
        var service = new PasswordHealthService(credentials);

        var reports = service.AnalyzeAll(VaultCredential);

        Assert.All(reports, r => Assert.False(r.IsReused));
    }

    [Fact]
    public void AnalyzeAll_WithAnEmptyVault_ReturnsAnEmptyReportRatherThanFailing()
    {
        // Failure path: "analysis fails to identify weak passwords" — an
        // empty vault must not be mistaken for an analysis failure.
        var credentials = CreateCredentialService();
        var service = new PasswordHealthService(credentials);

        var reports = service.AnalyzeAll(VaultCredential);

        Assert.Empty(reports);
    }

    [Fact]
    public void AnalyzeAll_WhenDecryptionFails_PropagatesRatherThanSilentlyIgnoringTheVault()
    {
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "Site", "user", "whatever-password", null);
        var service = new PasswordHealthService(credentials);

        Assert.Throws<VaultDecryptionFailedException>(() => service.AnalyzeAll("wrong-vault-credential"));
    }

    [Fact]
    public void AnalyzeAll_WithAuditLoggerProvided_LogsCountsWithoutLoggingAnyPasswordValue()
    {
        // Trust: analysis results are logged without exposing plaintext passwords.
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "Site", "user", "password123", null);
        credentials.Add(VaultCredential, "Other", "user2", "Tr0ub4dor&3-Zebra!", null);
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = new PasswordHealthService(credentials, logger);

        service.AnalyzeAll(VaultCredential);

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("AnalyzeAll", lines[0]);
        Assert.Contains("analyzed=2", lines[0]);
        Assert.Contains("weak=1", lines[0]);
        Assert.Contains("reused=0", lines[0]);
        Assert.DoesNotContain("password123", lines[0]);
        Assert.DoesNotContain("Tr0ub4dor&3-Zebra!", lines[0]);
    }

    [Fact]
    public async Task AnalyzeAllAsync_WhenBreachCheckerFlagsAPassword_ReportsItBreached()
    {
        // Acceptance: when breach awareness is enabled, compromised passwords are flagged.
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "Site", "user", "leaked-password", null);
        var breachChecker = new FakeBreachChecker().WithResult(
            "leaked-password", new BreachCheckResult { Outcome = BreachCheckOutcome.Breached, BreachCount = 999 });
        var service = new PasswordHealthService(credentials);

        var report = (await service.AnalyzeAllAsync(VaultCredential, breachChecker, CancellationToken.None)).Single();

        Assert.Equal(BreachCheckOutcome.Breached, report.BreachOutcome);
        Assert.Equal(999, report.BreachCount);
    }

    [Fact]
    public async Task AnalyzeAllAsync_WhenBreachCheckerCannotReach_ReportsUnavailableRatherThanClean()
    {
        // Failure path: "false positives in breach detection" — an
        // unreachable check must never be reported as a clean bill of health.
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "Site", "user", "some-password", null);
        var breachChecker = new FakeBreachChecker().WithResult(
            "some-password", new BreachCheckResult { Outcome = BreachCheckOutcome.CheckUnavailable });
        var service = new PasswordHealthService(credentials);

        var report = (await service.AnalyzeAllAsync(VaultCredential, breachChecker, CancellationToken.None)).Single();

        Assert.Equal(BreachCheckOutcome.CheckUnavailable, report.BreachOutcome);
        Assert.Null(report.BreachCount);
    }

    [Fact]
    public async Task AnalyzeAllAsync_NeverPassesAPasswordToTheAuditLogger()
    {
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "Site", "user", "leaked-password", null);
        var breachChecker = new FakeBreachChecker().WithResult(
            "leaked-password", new BreachCheckResult { Outcome = BreachCheckOutcome.Breached, BreachCount = 5 });
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = new PasswordHealthService(credentials, logger);

        await service.AnalyzeAllAsync(VaultCredential, breachChecker, CancellationToken.None);

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("breached=1", lines[0]);
        Assert.Contains("reused=0", lines[0]);
        Assert.DoesNotContain("leaked-password", lines[0]);
    }

    [Fact]
    public async Task AnalyzeAllAsync_WithTwoCredentialsSharingTheSamePassword_FlagsBothAsReused()
    {
        var credentials = CreateCredentialService();
        var id1 = credentials.Add(VaultCredential, "Site A", "userA", "shared-password-1", null);
        var id2 = credentials.Add(VaultCredential, "Site B", "userB", "shared-password-1", null);
        var breachChecker = new FakeBreachChecker();
        var service = new PasswordHealthService(credentials);

        var reports = await service.AnalyzeAllAsync(VaultCredential, breachChecker, CancellationToken.None);

        Assert.True(reports.Single(r => r.CredentialId == id1).IsReused);
        Assert.True(reports.Single(r => r.CredentialId == id2).IsReused);
    }
}
