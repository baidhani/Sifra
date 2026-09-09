namespace Sifra.Vault.Audit;

/// <summary>
/// Thrown when an audit entry could not be written even after retries. The
/// admin has already been alerted by the time this is thrown — this is not
/// a silent failure, it is a loud one with a clear signal at two levels.
/// </summary>
public sealed class AuditLoggingFailedException : Exception
{
    public AuditLoggingFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
