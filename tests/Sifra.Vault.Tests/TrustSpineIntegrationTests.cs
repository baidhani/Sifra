using Sifra.Vault;
using Sifra.Vault.Audit;
using Sifra.Vault.Auth;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

/// <summary>
/// Proves STORY-015's acceptance criterion 1 ("any vault operation... is
/// logged") end-to-end — that the audit logger wired into VaultService,
/// VaultAuthenticator and VaultDataService is actually invoked, not just
/// unit-tested in isolation.
/// </summary>
public sealed class TrustSpineIntegrationTests : IDisposable
{
    private readonly string _dataDirectory;

    public TrustSpineIntegrationTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-trustspine-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private AuditLogger CreateAuditLogger() =>
        new(new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log")), new LocalFakeAdminAlertSink());

    [Fact]
    public void CreateVault_WithAuditLoggerProvided_LogsTheOperation()
    {
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = new VaultService(new VaultStore(_dataDirectory), logger);

        service.CreateVault();

        Assert.Single(sink.ReadAll());
        Assert.Contains("CreateVault", sink.ReadAll()[0]);
    }

    [Fact]
    public void CreateVault_WithNoAuditLoggerProvided_StillWorksUnaffected()
    {
        // Backward compatibility: existing callers that don't pass a logger are unaffected.
        var service = new VaultService(new VaultStore(_dataDirectory));

        var recoveryKey = service.CreateVault();

        Assert.False(string.IsNullOrWhiteSpace(recoveryKey));
    }

    [Fact]
    public void Authenticate_WithAuditLoggerProvided_LogsTheOperationWithoutLoggingTheCredential()
    {
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var authenticator = new VaultAuthenticator(
            new VaultAccessCredentialStore(_dataDirectory),
            new FileAccessAuditLog(Path.Combine(_dataDirectory, "vault-access.log")),
            logger);
        authenticator.SetCredential("secret-password");

        authenticator.Authenticate("secret-password");

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("Authenticate", lines[0]);
        Assert.DoesNotContain("secret-password", lines[0]);
    }

    [Fact]
    public void EditAndSyncNow_WithAuditLoggerProvided_LogBothOperations()
    {
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var provider = new LocalFakeCloudSyncProvider { IsConnected = true };
        var dataService = new VaultDataService(
            new VaultDataStore(_dataDirectory),
            new OfflineChangeQueueStore(_dataDirectory),
            provider,
            logger);

        dataService.Edit(new VaultDataItem("item-1", "content", DateTimeOffset.UtcNow));
        dataService.SyncNow();

        var lines = sink.ReadAll();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Contains("Edit"));
        Assert.Contains(lines, l => l.Contains("SyncNow"));
    }
}
