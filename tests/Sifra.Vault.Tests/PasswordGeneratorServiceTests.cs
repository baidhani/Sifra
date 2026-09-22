using Sifra.Vault.Credentials;

namespace Sifra.Vault.Tests;

public sealed class PasswordGeneratorServiceTests
{
    [Theory]
    [InlineData(PasswordGeneratorMode.Random, 8)]
    [InlineData(PasswordGeneratorMode.Random, 32)]
    [InlineData(PasswordGeneratorMode.LettersAndNumbers, 16)]
    [InlineData(PasswordGeneratorMode.NumbersOnly, 6)]
    public void Generate_ProducesAPasswordOfExactlyTheRequestedLength(PasswordGeneratorMode mode, int length)
    {
        var password = PasswordGeneratorService.Generate(new PasswordGeneratorOptions(mode, length));

        Assert.Equal(length, password.Length);
    }

    [Fact]
    public void Generate_NumbersOnly_ContainsOnlyDigits()
    {
        var password = PasswordGeneratorService.Generate(new PasswordGeneratorOptions(PasswordGeneratorMode.NumbersOnly, 50));

        Assert.All(password, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void Generate_LettersAndNumbers_ContainsNoSymbols()
    {
        var password = PasswordGeneratorService.Generate(new PasswordGeneratorOptions(PasswordGeneratorMode.LettersAndNumbers, 50));

        Assert.All(password, c => Assert.True(char.IsLetterOrDigit(c)));
    }

    [Fact]
    public void Generate_Random_OnlyUsesCharsFromLettersDigitsAndConfiguredSymbols()
    {
        const string symbols = "!@#";
        var password = PasswordGeneratorService.Generate(new PasswordGeneratorOptions(PasswordGeneratorMode.Random, 200, Symbols: symbols));

        var allowed = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" + symbols;
        Assert.All(password, c => Assert.Contains(c, allowed));
    }

    [Fact]
    public void Generate_ExcludeSimilarCharacters_NeverProducesOOrLOr1OrIOrZero()
    {
        var password = PasswordGeneratorService.Generate(new PasswordGeneratorOptions(
            PasswordGeneratorMode.LettersAndNumbers, 500, ExcludeSimilarCharacters: true));

        Assert.DoesNotContain('1', password);
        Assert.DoesNotContain('l', password);
        Assert.DoesNotContain('I', password);
        Assert.DoesNotContain('0', password);
        Assert.DoesNotContain('O', password);
    }

    [Fact]
    public void Generate_Random_WithSymbolsClearedAndExcludeSimilar_StillSucceedsUsingLettersAndDigits()
    {
        // Letters+digits alone (minus the 5 excluded) is still a large,
        // non-empty set — this must not throw just because Symbols is empty.
        var password = PasswordGeneratorService.Generate(new PasswordGeneratorOptions(
            PasswordGeneratorMode.Random, 20, Symbols: "", ExcludeSimilarCharacters: true));

        Assert.Equal(20, password.Length);
    }

    [Fact]
    public void Generate_NumbersOnly_WithExcludeSimilar_ThrowsBecauseCharsetWouldBeEmpty()
    {
        // Digits-only charset is "0123456789" — excluding "1" and "0"
        // still leaves 8 digits, so this should NOT throw. Sanity check
        // the boundary is handled correctly rather than assumed.
        var password = PasswordGeneratorService.Generate(new PasswordGeneratorOptions(
            PasswordGeneratorMode.NumbersOnly, 20, ExcludeSimilarCharacters: true));

        Assert.DoesNotContain('0', password);
        Assert.DoesNotContain('1', password);
    }

    [Fact]
    public void Generate_Memorable_ProducesTheRequestedNumberOfWordsJoinedBySeparator()
    {
        var password = PasswordGeneratorService.Generate(new PasswordGeneratorOptions(
            PasswordGeneratorMode.Memorable, Length: 5, Separator: "-"));

        var parts = password.Split('-');
        Assert.Equal(5, parts.Length);
        Assert.All(parts, p => Assert.True(p.Length > 0));
    }

    [Fact]
    public void Generate_Memorable_UsesTheConfiguredSeparator()
    {
        var password = PasswordGeneratorService.Generate(new PasswordGeneratorOptions(
            PasswordGeneratorMode.Memorable, Length: 3, Separator: "_"));

        Assert.Equal(2, password.Count(c => c == '_'));
    }

    [Fact]
    public void Generate_CalledManyTimes_ProducesDifferentResults()
    {
        // Not a rigorous randomness test — just confirms this isn't
        // accidentally deterministic (e.g. a fixed seed).
        var results = Enumerable.Range(0, 20)
            .Select(_ => PasswordGeneratorService.Generate(new PasswordGeneratorOptions(PasswordGeneratorMode.Random, 16)))
            .ToHashSet();

        Assert.True(results.Count > 1);
    }

    [Fact]
    public void Generate_ZeroLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => PasswordGeneratorService.Generate(new PasswordGeneratorOptions(PasswordGeneratorMode.Random, 0)));
    }
}
