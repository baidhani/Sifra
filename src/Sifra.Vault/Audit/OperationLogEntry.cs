namespace Sifra.Vault.Audit;

/// <summary>
/// One entry in the vault's trust-spine audit trail. UserId is the OS-level
/// user running the process (Environment.UserName) — this app has no
/// account/identity system of its own, so that is the honest identifier
/// available today, not a fabricated one.
/// </summary>
public sealed record OperationLogEntry(
    string OperationId,
    string OperationName,
    string UserId,
    DateTimeOffset TimestampUtc,
    string? Details,
    bool Truncated);
