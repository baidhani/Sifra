using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;

namespace Sifra.Vault.Health;

/// <summary>
/// Local password-strength analysis. Reuses CredentialService for
/// decryption — no new crypto path. Never logs or returns a plaintext
/// password; only pass/fail reasons per credential.
/// </summary>
public sealed class PasswordHealthService
{
    private const int MinimumLength = 12;
    private const int MinimumCharacterClasses = 3;

    // A small, deliberately non-exhaustive sample of well-known weak/common
    // passwords for this walking skeleton's local check. Breach awareness
    // (a real, live check against actually-breached passwords) is a
    // separate concern — see IBreachChecker.
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "123456", "12345678", "123456789",
        "qwerty", "qwerty123", "letmein", "welcome", "admin", "iloveyou",
        "monkey", "dragon", "football", "baseball", "abc123", "111111",
        "sunshine", "princess", "trustno1", "master", "login", "starwars",
    };

    private readonly CredentialService _credentials;
    private readonly AuditLogger? _auditLogger;

    public PasswordHealthService(CredentialService credentials, AuditLogger? auditLogger = null)
    {
        _credentials = credentials;
        _auditLogger = auditLogger;
    }

    public IReadOnlyList<CredentialHealthReport> AnalyzeAll(string vaultCredential)
    {
        var summaries = _credentials.List(vaultCredential);
        var reports = new List<CredentialHealthReport>();

        foreach (var summary in summaries)
        {
            var full = _credentials.GetById(vaultCredential, summary.Id);
            var reasons = AnalyzePassword(full.Password!);
            reports.Add(new CredentialHealthReport
            {
                CredentialId = full.Id,
                Label = full.Label,
                IsWeak = reasons.Count > 0,
                Reasons = reasons,
            });
        }

        var weakCount = reports.Count(r => r.IsWeak);
        _auditLogger?.Log(nameof(AnalyzeAll), Environment.UserName, details: $"analyzed={reports.Count} weak={weakCount}");

        return reports;
    }

    /// <summary>
    /// Same local weakness analysis as AnalyzeAll, plus a live breach check
    /// per credential. Acceptance: "when breach awareness is enabled" —
    /// this is the enabled path; AnalyzeAll remains available for
    /// local-only analysis with no network involved.
    /// </summary>
    public async Task<IReadOnlyList<CredentialHealthReport>> AnalyzeAllAsync(
        string vaultCredential, IBreachChecker breachChecker, CancellationToken cancellationToken)
    {
        var summaries = _credentials.List(vaultCredential);
        var reports = new List<CredentialHealthReport>();

        foreach (var summary in summaries)
        {
            var full = _credentials.GetById(vaultCredential, summary.Id);
            var reasons = AnalyzePassword(full.Password!);
            var breach = await breachChecker.CheckAsync(full.Password!, cancellationToken);

            reports.Add(new CredentialHealthReport
            {
                CredentialId = full.Id,
                Label = full.Label,
                IsWeak = reasons.Count > 0,
                Reasons = reasons,
                BreachOutcome = breach.Outcome,
                BreachCount = breach.BreachCount,
            });
        }

        var weakCount = reports.Count(r => r.IsWeak);
        var breachedCount = reports.Count(r => r.BreachOutcome == BreachCheckOutcome.Breached);
        var unavailableCount = reports.Count(r => r.BreachOutcome == BreachCheckOutcome.CheckUnavailable);
        _auditLogger?.Log(nameof(AnalyzeAllAsync), Environment.UserName,
            details: $"analyzed={reports.Count} weak={weakCount} breached={breachedCount} breachCheckUnavailable={unavailableCount}");

        return reports;
    }

    private static IReadOnlyList<PasswordWeaknessReason> AnalyzePassword(string password)
    {
        var reasons = new List<PasswordWeaknessReason>();

        if (password.Length < MinimumLength)
        {
            reasons.Add(PasswordWeaknessReason.TooShort);
        }

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        if (classes < MinimumCharacterClasses)
        {
            reasons.Add(PasswordWeaknessReason.LowCharacterVariety);
        }

        if (CommonPasswords.Contains(password))
        {
            reasons.Add(PasswordWeaknessReason.CommonPassword);
        }

        return reasons;
    }
}
