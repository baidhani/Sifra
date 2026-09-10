namespace Sifra.Vault.Autofill;

/// <summary>Thrown when a fill is attempted without explicit consent — the "autofill occurs without user consent" failure path.</summary>
public sealed class AutofillConsentRequiredException : Exception
{
    public AutofillConsentRequiredException()
        : base("Autofill requires explicit user consent.")
    {
    }
}
