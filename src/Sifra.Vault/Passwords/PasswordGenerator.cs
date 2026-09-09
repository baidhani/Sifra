using Sifra.Vault.Audit;

namespace Sifra.Vault.Passwords;

/// <summary>
/// Generates strong passwords: cryptographically random, guaranteed to
/// include at least one lowercase, uppercase, digit, and symbol
/// character. Never logs the generated password itself — only that
/// generation happened, when, and for whom.
/// </summary>
public sealed class PasswordGenerator
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}";
    private const string AllCharacters = Lowercase + Uppercase + Digits + Symbols;

    private readonly IRandomIndexSource _randomSource;
    private readonly AuditLogger? _auditLogger;

    public PasswordGenerator(IRandomIndexSource? randomSource = null, AuditLogger? auditLogger = null)
    {
        _randomSource = randomSource ?? new SecureRandomIndexSource();
        _auditLogger = auditLogger;
    }

    /// <exception cref="UnsupportedPasswordLengthException">The requested length is outside [MinLength, MaxLength].</exception>
    /// <exception cref="PasswordGenerationFailedException">The random source failed.</exception>
    public string Generate(int length)
    {
        if (length < MinLength || length > MaxLength)
        {
            throw new UnsupportedPasswordLengthException(
                $"Password length must be between {MinLength} and {MaxLength} (requested {length}).");
        }

        string password;
        try
        {
            password = BuildPassword(length);
        }
        catch (Exception ex) when (ex is not UnsupportedPasswordLengthException)
        {
            throw new PasswordGenerationFailedException("Could not generate a password.", ex);
        }

        // Never log the password value itself — only that generation happened.
        _auditLogger?.Log(nameof(Generate), Environment.UserName, details: $"length={length}");

        return password;
    }

    private string BuildPassword(int length)
    {
        var characters = new List<char>(length)
        {
            PickChar(Lowercase),
            PickChar(Uppercase),
            PickChar(Digits),
            PickChar(Symbols)
        };

        for (int i = characters.Count; i < length; i++)
        {
            characters.Add(PickChar(AllCharacters));
        }

        Shuffle(characters);
        return new string(characters.ToArray());
    }

    private char PickChar(string pool) => pool[_randomSource.NextInt32(pool.Length)];

    private void Shuffle(List<char> characters)
    {
        for (int i = characters.Count - 1; i > 0; i--)
        {
            int j = _randomSource.NextInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }
    }
}
