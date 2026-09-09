using System.Security.Cryptography;
using System.Text;

namespace Sifra.Vault;

/// <summary>
/// Generates a human-readable, high-entropy recovery key. The key itself is
/// never persisted anywhere — only its hash is (see VaultService).
/// </summary>
internal static class RecoveryKeyGenerator
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int KeyLengthBytes = 20; // 160 bits of entropy
    private const int GroupSize = 4;

    public static string Generate()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(KeyLengthBytes);
        var encoded = ToBase32(keyBytes);
        return GroupWithDashes(encoded, GroupSize);
    }

    private static string ToBase32(byte[] data)
    {
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bitsInBuffer = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                int index = (buffer >> bitsInBuffer) & 0b11111;
                result.Append(Base32Alphabet[index]);
            }
        }

        if (bitsInBuffer > 0)
        {
            int index = (buffer << (5 - bitsInBuffer)) & 0b11111;
            result.Append(Base32Alphabet[index]);
        }

        return result.ToString();
    }

    private static string GroupWithDashes(string value, int groupSize)
    {
        var result = new StringBuilder(value.Length + value.Length / groupSize);
        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0 && i % groupSize == 0)
            {
                result.Append('-');
            }
            result.Append(value[i]);
        }
        return result.ToString();
    }
}
