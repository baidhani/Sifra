namespace Sifra.Vault.Audit;

/// <summary>
/// The vault's trust spine: every logged operation gets a unique id, a
/// timestamp, and the acting user id. Log-write failures are retried a
/// capped number of times, then raise an admin alert and a clear
/// exception — never swallowed silently.
/// </summary>
public sealed class AuditLogger
{
    private const int MaxIdGenerationAttempts = 5;

    private readonly IAuditLogSink _sink;
    private readonly IAdminAlertSink _alertSink;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly int _maxDetailsLength;
    private readonly Func<string> _idGenerator;
    private readonly HashSet<string> _issuedIds = new();
    private readonly object _issuedIdsLock = new();

    public AuditLogger(
        IAuditLogSink sink,
        IAdminAlertSink alertSink,
        int maxAttempts = 3,
        TimeSpan? retryDelay = null,
        int maxDetailsLength = 4000,
        Func<string>? idGenerator = null)
    {
        _sink = sink;
        _alertSink = alertSink;
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? TimeSpan.Zero;
        _maxDetailsLength = maxDetailsLength;
        _idGenerator = idGenerator ?? (() => Guid.NewGuid().ToString("N"));
    }

    /// <returns>The unique operation id assigned to this log entry.</returns>
    /// <exception cref="OperationIdCollisionException">A unique operation id could not be generated.</exception>
    /// <exception cref="AuditLoggingFailedException">The entry could not be written after all retry attempts. The admin has already been alerted.</exception>
    public string Log(string operationName, string userId, string? details = null)
    {
        var operationId = GenerateUniqueOperationId();
        var entry = BuildEntry(operationId, operationName, userId, details, _maxDetailsLength);

        Exception? lastError = null;
        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                _sink.Write(entry);
                return operationId;
            }
            catch (AuditSinkUnavailableException ex)
            {
                lastError = ex;
                if (attempt < _maxAttempts && _retryDelay > TimeSpan.Zero)
                {
                    Thread.Sleep(_retryDelay);
                }
            }
        }

        _alertSink.Alert($"Audit logging failed after {_maxAttempts} attempts for operation '{operationName}' (id={operationId}).");
        throw new AuditLoggingFailedException(
            $"Could not write audit log entry for operation '{operationName}' after {_maxAttempts} attempts.",
            lastError!);
    }

    private static OperationLogEntry BuildEntry(string operationId, string operationName, string userId, string? details, int maxDetailsLength)
    {
        var truncated = false;
        if (details is not null && details.Length > maxDetailsLength)
        {
            details = details[..maxDetailsLength] + "...[truncated]";
            truncated = true;
        }

        return new OperationLogEntry(operationId, operationName, userId, DateTimeOffset.UtcNow, details, truncated);
    }

    private string GenerateUniqueOperationId()
    {
        for (int attempt = 0; attempt < MaxIdGenerationAttempts; attempt++)
        {
            var candidate = _idGenerator();
            lock (_issuedIdsLock)
            {
                if (_issuedIds.Add(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new OperationIdCollisionException(
            $"Could not generate a unique operation id after {MaxIdGenerationAttempts} attempts.");
    }
}
