using System.Security.Cryptography;

namespace Sifra.Vault.Credentials;

/// <summary>
/// Generates TOTP codes (RFC 6238, the standard behind Google/Microsoft
/// Authenticator-style 2FA) from a Base32 secret — the value stored in a
/// CustomField of type OneTimePassword. Pure, deterministic, no I/O: given
/// the same secret and time, always produces the same code, which is
/// exactly what lets it be verified against RFC 6238's own published test
/// vectors instead of just trusting it "looks right".
/// </summary>
public static class TotpGenerator
{
    private const int DefaultDigits = 6;
    private static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(30);

    /// <exception cref="FormatException">The secret is not valid Base32.</exception>
    public static string GenerateCode(string base32Secret, DateTimeOffset? at = null, int digits = DefaultDigits, TimeSpan? period = null)
    {
        var key = Base32Decode(base32Secret);
        var counter = (long)((at ?? DateTimeOffset.UtcNow) - DateTimeOffset.UnixEpoch).TotalSeconds / (long)(period ?? DefaultPeriod).TotalSeconds;

        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                          | ((hash[offset + 1] & 0xFF) << 16)
                          | ((hash[offset + 2] & 0xFF) << 8)
                          | (hash[offset + 3] & 0xFF);

        var code = binaryCode % (int)Math.Pow(10, digits);
        return code.ToString().PadLeft(digits, '0');
    }

    /// <summary>Seconds remaining before the current code expires — for a UI countdown.</summary>
    public static int SecondsRemaining(DateTimeOffset? at = null, TimeSpan? period = null)
    {
        var periodSeconds = (long)(period ?? DefaultPeriod).TotalSeconds;
        var elapsed = (long)((at ?? DateTimeOffset.UtcNow) - DateTimeOffset.UnixEpoch).TotalSeconds % periodSeconds;
        return (int)(periodSeconds - elapsed);
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = base32.Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", "");

        var bits = 0;
        var value = 0;
        var output = new List<byte>();

        foreach (var c in cleaned)
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
            {
                throw new FormatException($"'{c}' is not a valid Base32 character.");
            }

            value = (value << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
