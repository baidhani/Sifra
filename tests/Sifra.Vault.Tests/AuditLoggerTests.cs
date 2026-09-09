using Sifra.Vault.Audit;

namespace Sifra.Vault.Tests;

public sealed class AuditLoggerTests : IDisposable
{
    private readonly string _dataDirectory;
    private readonly string _logPath;

    public AuditLoggerTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-audit-tests-" + Guid.NewGuid());
        _logPath = Path.Combine(_dataDirectory, "operations.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Log_OnSuccess_WritesTimestampUserIdAndUniqueOperationId()
    {
        var sink = new FileAuditLogSink(_logPath);
        var alerts = new LocalFakeAdminAlertSink();
        var logger = new AuditLogger(sink, alerts);

        var operationId = logger.Log("CreateVault", "firas");

        Assert.False(string.IsNullOrWhiteSpace(operationId));
        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains(operationId, lines[0]);
        Assert.Contains("CreateVault", lines[0]);
        Assert.Contains("firas", lines[0]);
        Assert.Empty(alerts.Alerts);
    }

    [Fact]
    public void Log_TwoOperations_GetDifferentOperationIds()
    {
        var logger = new AuditLogger(new FileAuditLogSink(_logPath), new LocalFakeAdminAlertSink());

        var id1 = logger.Log("CreateVault", "firas");
        var id2 = logger.Log("Authenticate", "firas");

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Log_WhenSinkFailsThenRecovers_SucceedsWithinRetryBudget()
    {
        // "Logging service is unavailable" — but recovers before retries are exhausted.
        var sink = new FlakySink(failuresBeforeSuccess: 2);
        var alerts = new LocalFakeAdminAlertSink();
        var logger = new AuditLogger(sink, alerts, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var operationId = logger.Log("CreateVault", "firas");

        Assert.False(string.IsNullOrWhiteSpace(operationId));
        Assert.Equal(3, sink.AttemptCount); // failed twice, succeeded on the 3rd
        Assert.Empty(alerts.Alerts); // recovered — no alert needed
    }

    [Fact]
    public void Log_WhenSinkNeverRecovers_AlertsAdminAndThrowsAfterExhaustingRetries()
    {
        // Failure path: "logging service is unavailable" beyond the retry budget.
        var sink = new FlakySink(failuresBeforeSuccess: int.MaxValue);
        var alerts = new LocalFakeAdminAlertSink();
        var logger = new AuditLogger(sink, alerts, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var ex = Assert.Throws<AuditLoggingFailedException>(() => logger.Log("CreateVault", "firas"));

        Assert.Equal(3, sink.AttemptCount);
        Assert.Single(alerts.Alerts);
        Assert.Contains("CreateVault", alerts.Alerts[0]);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Log_WhenIdGeneratorCollidesOnce_RecoversWithADifferentId()
    {
        // Failure path: "operation ID collision" — detected and resolved, not silently overwritten.
        var idsToReturn = new Queue<string>(new[] { "dup-id", "dup-id", "unique-id" });
        var logger = new AuditLogger(
            new FileAuditLogSink(_logPath),
            new LocalFakeAdminAlertSink(),
            idGenerator: () => idsToReturn.Dequeue());

        var firstId = logger.Log("CreateVault", "firas"); // consumes "dup-id"
        var secondId = logger.Log("Authenticate", "firas"); // "dup-id" collides, retries to "unique-id"

        Assert.Equal("dup-id", firstId);
        Assert.Equal("unique-id", secondId);
    }

    [Fact]
    public void Log_WhenIdGeneratorNeverProducesAUniqueId_ThrowsInsteadOfOverwritingAnEntry()
    {
        var logger = new AuditLogger(
            new FileAuditLogSink(_logPath),
            new LocalFakeAdminAlertSink(),
            idGenerator: () => "always-the-same-id");

        logger.Log("CreateVault", "firas"); // claims "always-the-same-id"

        Assert.Throws<OperationIdCollisionException>(() => logger.Log("Authenticate", "firas"));
    }

    [Fact]
    public void Log_WithOversizedDetails_TruncatesRatherThanFailingOrSilentlyDropping()
    {
        // Failure path: "log entry exceeds size limit."
        var sink = new FileAuditLogSink(_logPath);
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink(), maxDetailsLength: 10);

        logger.Log("CreateVault", "firas", details: "this text is definitely longer than ten characters");

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("[truncated]", lines[0]);
        Assert.Contains("\"Truncated\":true", lines[0]);
    }

    /// <summary>Test double that fails Write() a configurable number of times before succeeding.</summary>
    private sealed class FlakySink : IAuditLogSink
    {
        private readonly int _failuresBeforeSuccess;
        public int AttemptCount { get; private set; }

        public FlakySink(int failuresBeforeSuccess) => _failuresBeforeSuccess = failuresBeforeSuccess;

        public void Write(OperationLogEntry entry)
        {
            AttemptCount++;
            if (AttemptCount <= _failuresBeforeSuccess)
            {
                throw new AuditSinkUnavailableException("Simulated logging service outage.");
            }
        }
    }
}
