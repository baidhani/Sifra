namespace Sifra.Vault.Autofill;

public enum CapturedLoginStatus
{
    /// <summary>No credential exists for this username on this domain — offer to save it as new.</summary>
    New,

    /// <summary>A matching credential exists and its password is already exactly what was just submitted — nothing to do.</summary>
    Unchanged,

    /// <summary>A matching credential exists but its stored password differs from what was just submitted — offer to update it.</summary>
    Different,
}

/// <summary>Result of <see cref="CredentialAutofillService.CheckCapturedLogin"/>.</summary>
public sealed record CapturedLoginCheck(CapturedLoginStatus Status, string? CredentialId, string? ExistingLabel);
