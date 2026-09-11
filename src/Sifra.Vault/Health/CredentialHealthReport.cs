namespace Sifra.Vault.Health;

/// <summary>
/// Result of analyzing one credential's password. Never carries the
/// password itself — only whether it's weak and why.
/// </summary>
public sealed class CredentialHealthReport
{
    public required string CredentialId { get; init; }
    public required string Label { get; init; }
    public required bool IsWeak { get; init; }
    public required IReadOnlyList<PasswordWeaknessReason> Reasons { get; init; }

    /// <summary>True when this exact password is also used by at least one other credential in the vault.</summary>
    public required bool IsReused { get; init; }

    /// <summary>Null when breach checking wasn't run for this analysis.</summary>
    public BreachCheckOutcome? BreachOutcome { get; init; }
    public int? BreachCount { get; init; }
}
