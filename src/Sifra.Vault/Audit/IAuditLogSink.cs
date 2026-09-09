namespace Sifra.Vault.Audit;

/// <summary>
/// Where audit entries are written. A local file today; a real logging
/// service (e.g. a hosted log platform) could implement this later without
/// AuditLogger's retry/alert/id logic changing at all.
/// </summary>
public interface IAuditLogSink
{
    /// <exception cref="AuditSinkUnavailableException">The entry could not be written.</exception>
    void Write(OperationLogEntry entry);
}
