using Sifra.Vault.Credentials;

namespace Sifra.Vault.Tests;

/// <summary>
/// Verified against RFC 6238's own published SHA-1 test vectors (Appendix
/// B), not just "the code looks plausible" — the standard's test secret is
/// the ASCII string "12345678901234567890", Base32-encoded here since
/// GenerateCode takes a Base32 secret like a real authenticator app would.
/// </summary>
public sealed class TotpGeneratorTests
{
    private const string Rfc6238TestSecretBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59, "94287082")]
    [InlineData(1111111109, "07081804")]
    [InlineData(1111111111, "14050471")]
    [InlineData(1234567890, "89005924")]
    [InlineData(2000000000, "69279037")]
    public void GenerateCode_MatchesRfc6238PublishedTestVectors(long unixSeconds, string expectedCode)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        var code = TotpGenerator.GenerateCode(Rfc6238TestSecretBase32, at, digits: 8);

        Assert.Equal(expectedCode, code);
    }

    [Fact]
    public void GenerateCode_DefaultsToSixDigits()
    {
        var code = TotpGenerator.GenerateCode(Rfc6238TestSecretBase32, DateTimeOffset.FromUnixTimeSeconds(59));

        Assert.Equal(6, code.Length);
    }

    [Fact]
    public void GenerateCode_IsStableWithinTheSameThirtySecondWindow()
    {
        var a = TotpGenerator.GenerateCode(Rfc6238TestSecretBase32, DateTimeOffset.FromUnixTimeSeconds(1000000000));
        var b = TotpGenerator.GenerateCode(Rfc6238TestSecretBase32, DateTimeOffset.FromUnixTimeSeconds(1000000015));

        Assert.Equal(a, b);
    }

    [Fact]
    public void GenerateCode_ChangesInTheNextThirtySecondWindow()
    {
        var a = TotpGenerator.GenerateCode(Rfc6238TestSecretBase32, DateTimeOffset.FromUnixTimeSeconds(1000000000));
        var b = TotpGenerator.GenerateCode(Rfc6238TestSecretBase32, DateTimeOffset.FromUnixTimeSeconds(1000000031));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GenerateCode_WithAnInvalidBase32Character_ThrowsRatherThanSilentlyProducingAWrongCode()
    {
        // Failure path: a corrupted/mistyped secret must not silently
        // generate a plausible-looking but wrong code.
        Assert.Throws<FormatException>(() => TotpGenerator.GenerateCode("not-valid-base32!!!"));
    }

    [Fact]
    public void SecondsRemaining_IsWithinTheThirtySecondPeriod()
    {
        var remaining = TotpGenerator.SecondsRemaining(DateTimeOffset.FromUnixTimeSeconds(1000000010));

        Assert.InRange(remaining, 1, 30);
    }
}
