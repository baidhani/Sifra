namespace Sifra.Vault.Health;

/// <summary>1-4: how many of the strength meter's 4 segments should fill, and how they should read.</summary>
public enum PasswordStrengthLevel
{
    VeryWeak = 1,
    Weak = 2,
    Fair = 3,
    Strong = 4,
}

public sealed record PasswordStrengthEstimate(PasswordStrengthLevel Level, int FilledSegments, string CrackTimeDisplay);

/// <summary>
/// Local, offline crack-time estimate for the strength meter shown under a
/// Password field. Pure character-set/length entropy — no dictionary or
/// pattern check (that's PasswordHealthService's job for the health report)
/// — so this is a rough, fast, always-available estimate for the detail
/// pane, not a security verdict.
/// </summary>
public static class PasswordStrengthEstimator
{
    // A deliberately conservative (i.e. fast) offline attack rate against an
    // unsalted/fast hash, so the estimate never overstates how long a
    // password would hold up. Real attacker throughput varies wildly; this
    // is a rough guide for the user, not a guarantee.
    private const double GuessesPerSecond = 10_000_000_000;

    public static PasswordStrengthEstimate Estimate(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return new PasswordStrengthEstimate(PasswordStrengthLevel.VeryWeak, 0, "instantly");
        }

        var charsetSize = CharsetSize(password);
        var entropyBits = password.Length * Math.Log2(Math.Max(charsetSize, 1));
        var combinations = Math.Pow(2, entropyBits);
        var secondsToCrack = combinations / 2 / GuessesPerSecond;

        var level = LevelFor(entropyBits);
        var filled = (int)level;

        return new PasswordStrengthEstimate(level, filled, FormatCrackTime(secondsToCrack));
    }

    private static int CharsetSize(string password)
    {
        var size = 0;
        if (password.Any(char.IsLower)) size += 26;
        if (password.Any(char.IsUpper)) size += 26;
        if (password.Any(char.IsDigit)) size += 10;
        if (password.Any(c => !char.IsLetterOrDigit(c))) size += 32;
        return size;
    }

    private static PasswordStrengthLevel LevelFor(double entropyBits) => entropyBits switch
    {
        < 28 => PasswordStrengthLevel.VeryWeak,
        < 36 => PasswordStrengthLevel.Weak,
        < 60 => PasswordStrengthLevel.Fair,
        _ => PasswordStrengthLevel.Strong,
    };

    private static string FormatCrackTime(double seconds)
    {
        if (seconds < 1) return "instantly";
        if (seconds < 60) return FormatUnit(seconds, 1, "second");
        if (seconds < 3600) return FormatUnit(seconds, 60, "minute");
        if (seconds < 86_400d) return FormatUnit(seconds, 3600, "hour");
        if (seconds < 86_400d * 30) return FormatUnit(seconds, 86_400, "day");
        if (seconds < 86_400d * 365) return FormatUnit(seconds, 86_400 * 30, "month");
        if (seconds < 86_400d * 365 * 100) return FormatUnit(seconds, 86_400 * 365, "year");
        return "centuries";
    }

    private static string FormatUnit(double seconds, double unitSeconds, string unitName)
    {
        var count = (int)(seconds / unitSeconds);
        if (count < 1) count = 1;
        return $"{count} {unitName}{(count == 1 ? "" : "s")}";
    }
}
