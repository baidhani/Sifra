namespace Sifra.Vault.Audit;

/// <summary>Thrown by an IAuditLogSink when it cannot write an entry — the "logging service is unavailable" failure path.</summary>
public sealed class AuditSinkUnavailableException : Exception
{
    public AuditSinkUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
