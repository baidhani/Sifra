using System.Security.Cryptography;
using System.Text;

namespace Sifra.Vault.Credentials;

/// <summary>
/// Generates passwords/passphrases for the Generator tool. Every random
/// choice goes through RandomNumberGenerator (cryptographically secure,
/// unbiased via GetInt32) — a password generator is exactly the kind of
/// place System.Random's non-cryptographic PRNG must never be used.
/// </summary>
public static class PasswordGeneratorService
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";

    // Exactly the characters called out as "similar" in the Password
    // Options screen — not a broader ambiguous-character list, so this
    // stays predictable for the user reading that label.
    private const string SimilarCharacters = "1lI0O";

    /// <exception cref="ArgumentException">Length/word count is out of range, or Random mode's effective character set is empty (e.g. Symbols cleared with ExcludeSimilarCharacters leaving nothing).</exception>
    public static string Generate(PasswordGeneratorOptions options)
    {
        if (options.Length < 1)
        {
            throw new ArgumentException("Length must be at least 1.", nameof(options));
        }

        return options.Mode switch
        {
            PasswordGeneratorMode.Memorable => GenerateMemorable(options),
            PasswordGeneratorMode.LettersAndNumbers => GenerateFromCharset(options.Length, ExcludeSimilar(Lowercase + Uppercase + Digits, options)),
            PasswordGeneratorMode.NumbersOnly => GenerateFromCharset(options.Length, ExcludeSimilar(Digits, options)),
            PasswordGeneratorMode.Random => GenerateFromCharset(options.Length, ExcludeSimilar(Lowercase + Uppercase + Digits + options.Symbols, options)),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
    }

    private static string ExcludeSimilar(string charset, PasswordGeneratorOptions options)
    {
        if (!options.ExcludeSimilarCharacters)
        {
            return charset;
        }

        var filtered = new string(charset.Where(c => !SimilarCharacters.Contains(c)).ToArray());
        return filtered;
    }

    private static string GenerateFromCharset(int length, string charset)
    {
        if (charset.Length == 0)
        {
            throw new ArgumentException("The character set for this generator mode is empty — check Symbols/Exclude similar characters in Password Options.");
        }

        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(charset[RandomNumberGenerator.GetInt32(charset.Length)]);
        }
        return builder.ToString();
    }

    private static string GenerateMemorable(PasswordGeneratorOptions options)
    {
        var words = PasswordGeneratorWordList.Words;
        var picked = new string[options.Length];
        for (var i = 0; i < options.Length; i++)
        {
            picked[i] = words[RandomNumberGenerator.GetInt32(words.Length)];
        }
        return string.Join(options.Separator, picked);
    }
}
