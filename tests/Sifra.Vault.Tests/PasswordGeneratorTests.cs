using Sifra.Vault.Audit;
using Sifra.Vault.Passwords;

namespace Sifra.Vault.Tests;

public sealed class PasswordGeneratorTests : IDisposable
{
    private readonly string _dataDirectory;

    public PasswordGeneratorTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-password-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Generate_ReturnsAPasswordOfTheRequestedLength()
    {
        // Acceptance: requesting a new password generates a strong password.
        var generator = new PasswordGenerator();

        var password = generator.Generate(16);

        Assert.Equal(16, password.Length);
    }

    [Fact]
    public void Generate_IncludesAtLeastOneOfEachCharacterClass()
    {
        var generator = new PasswordGenerator();

        var password = generator.Generate(20);

        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => !char.IsLetterOrDigit(c));
    }

    [Fact]
    public void Generate_CalledTwice_ProducesDifferentPasswords()
    {
        var generator = new PasswordGenerator();

        var first = generator.Generate(16);
        var second = generator.Generate(16);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(PasswordGenerator.MinLength - 1)]
    [InlineData(PasswordGenerator.MaxLength + 1)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Generate_WithUnsupportedLength_ThrowsWithAClearErrorMessage(int invalidLength)
    {
        // Acceptance: an invalid length produces an error message, not a crash or a silently-clamped password.
        var generator = new PasswordGenerator();

        var ex = Assert.Throws<UnsupportedPasswordLengthException>(() => generator.Generate(invalidLength));
        Assert.Contains(invalidLength.ToString(), ex.Message);
    }

    [Fact]
    public void Generate_WhenTheRandomSourceFails_ThrowsPasswordGenerationFailedException()
    {
        // Failure path: "password generation fails due to service error."
        var generator = new PasswordGenerator(new AlwaysFailingRandomSource());

        var ex = Assert.Throws<PasswordGenerationFailedException>(() => generator.Generate(16));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Generate_WithAuditLoggerProvided_LogsTheEventWithoutEverLoggingThePasswordValue()
    {
        // Trust: generated passwords are logged with a timestamp — but never the password itself.
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var generator = new PasswordGenerator(auditLogger: logger);

        var password = generator.Generate(16);

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("Generate", lines[0]);
        Assert.Contains("length=16", lines[0]);
        Assert.DoesNotContain(password, lines[0]);
    }

    [Fact]
    public void Generate_WhenLoggingServiceIsUnavailable_PropagatesAClearException()
    {
        // Failure path: "logging service is unavailable" — reuses AuditLogger's
        // already-tested retry+alert behavior (STORY-015); this proves the
        // integration actually surfaces it, not that it's silently ignored.
        var alerts = new LocalFakeAdminAlertSink();
        var logger = new AuditLogger(new AlwaysFailingSink(), alerts, maxAttempts: 2, retryDelay: TimeSpan.Zero);
        var generator = new PasswordGenerator(auditLogger: logger);

        Assert.Throws<AuditLoggingFailedException>(() => generator.Generate(16));
        Assert.Single(alerts.Alerts);
    }

    private sealed class AlwaysFailingRandomSource : IRandomIndexSource
    {
        public int NextInt32(int maxExclusiveValue) => throw new InvalidOperationException("Simulated random source outage.");
    }

    private sealed class AlwaysFailingSink : IAuditLogSink
    {
        public void Write(OperationLogEntry entry) => throw new AuditSinkUnavailableException("Simulated logging service outage.");
    }
}
