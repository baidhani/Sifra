using Sifra.Vault.Audit;
using Sifra.Vault.Autofill;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Tests;

public sealed class CredentialAutofillServiceTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public CredentialAutofillServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-autofill-tests-" + Guid.NewGuid());
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

    private CredentialAutofillService CreateService(AuditLogger? auditLogger = null) =>
        new(CreateCredentialService(), auditLogger);

    [Fact]
    public void OfferCredentialsForUrl_WithAMatchingCredential_OffersIt()
    {
        // Acceptance: visiting a login page offers matching credentials.
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com/settings");
        var service = new CredentialAutofillService(credentials);

        var offered = service.OfferCredentialsForUrl(VaultCredential, "https://github.com/login");

        Assert.Single(offered);
        Assert.Equal("GitHub", offered[0].Label);
    }

    [Fact]
    public void OfferCredentialsForUrl_WithNoMatchingCredential_OffersNothing()
    {
        var credentials = CreateCredentialService();
        credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var service = new CredentialAutofillService(credentials);

        var offered = service.OfferCredentialsForUrl(VaultCredential, "https://example.com/login");

        Assert.Empty(offered);
    }

    [Fact]
    public void FillCredential_WithConsentGranted_ReturnsTheCredential()
    {
        // Acceptance (implicit happy path for fill): consenting fills it.
        var credentials = CreateCredentialService();
        var id = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var service = new CredentialAutofillService(credentials);

        var filled = service.FillCredential(VaultCredential, id, "https://github.com/login", consentGranted: true);

        Assert.Equal("firas", filled.Username);
        Assert.Equal("hunter2", filled.Password);
    }

    [Fact]
    public void FillCredential_WithoutConsent_ThrowsAndFillsNothing()
    {
        // Acceptance: denying autofill means credentials are not filled.
        // Failure path: "autofill occurs without user consent."
        var credentials = CreateCredentialService();
        var id = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var service = new CredentialAutofillService(credentials);

        Assert.Throws<AutofillConsentRequiredException>(
            () => service.FillCredential(VaultCredential, id, "https://github.com/login", consentGranted: false));
    }

    [Fact]
    public void FillCredential_ForACredentialFromADifferentDomain_ThrowsDomainMismatch()
    {
        // Failure path: "incorrect credentials filled" — a credential id
        // must actually belong to the page being filled, not just exist.
        var credentials = CreateCredentialService();
        var githubId = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var service = new CredentialAutofillService(credentials);

        Assert.Throws<CredentialDomainMismatchException>(
            () => service.FillCredential(VaultCredential, githubId, "https://evil.example.com/login", consentGranted: true));
    }

    [Fact]
    public void FillCredential_ForANonExistentCredentialId_ThrowsDomainMismatchRatherThanExposingExistence()
    {
        var credentials = CreateCredentialService();
        var service = new CredentialAutofillService(credentials);

        Assert.Throws<CredentialDomainMismatchException>(
            () => service.FillCredential(VaultCredential, "does-not-exist", "https://github.com/login", consentGranted: true));
    }

    [Fact]
    public void FillCredential_WhenDecryptionFails_PropagatesRatherThanSilentlyFailing()
    {
        // Failure path: "autofill fails on supported sites" — a real
        // failure (here: wrong vault credential at fill time) must be
        // surfaced loudly, not silently return empty/garbage.
        var credentials = CreateCredentialService();
        var id = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var service = new CredentialAutofillService(credentials);

        Assert.Throws<VaultDecryptionFailedException>(
            () => service.FillCredential("wrong-password", id, "https://github.com/login", consentGranted: true));
    }

    [Fact]
    public void FillCredential_WithAuditLoggerProvided_LogsWithoutLoggingTheUsernameOrPassword()
    {
        // Trust: autofill actions are logged.
        var credentials = CreateCredentialService();
        var id = credentials.Add(VaultCredential, "GitHub", "credential-username-should-not-leak", "hunter2", "https://github.com");
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = new CredentialAutofillService(credentials, logger);

        service.OfferCredentialsForUrl(VaultCredential, "https://github.com/login");
        service.FillCredential(VaultCredential, id, "https://github.com/login", consentGranted: true);

        var lines = sink.ReadAll();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Contains("OfferCredentialsForUrl"));
        Assert.Contains(lines, l => l.Contains("FillCredential") && l.Contains("outcome=filled"));
        Assert.All(lines, l => Assert.DoesNotContain("credential-username-should-not-leak", l));
        Assert.All(lines, l => Assert.DoesNotContain("hunter2", l));
    }

    [Fact]
    public void FillCredential_WhenConsentDenied_StillLogsTheDenial()
    {
        var credentials = CreateCredentialService();
        var id = credentials.Add(VaultCredential, "GitHub", "firas", "hunter2", "https://github.com");
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = new CredentialAutofillService(credentials, logger);

        Assert.Throws<AutofillConsentRequiredException>(
            () => service.FillCredential(VaultCredential, id, "https://github.com/login", consentGranted: false));

        var lines = sink.ReadAll();
        Assert.Contains(lines, l => l.Contains("FillCredential") && l.Contains("outcome=consent_denied"));
    }
}
