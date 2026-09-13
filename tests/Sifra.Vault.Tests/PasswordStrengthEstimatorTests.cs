using Sifra.Vault.Health;
using Xunit;

namespace Sifra.Vault.Tests;

public class PasswordStrengthEstimatorTests
{
    [Fact]
    public void EmptyPassword_IsVeryWeakAndInstant()
    {
        var estimate = PasswordStrengthEstimator.Estimate(string.Empty);

        Assert.Equal(PasswordStrengthLevel.VeryWeak, estimate.Level);
        Assert.Equal(0, estimate.FilledSegments);
        Assert.Equal("instantly", estimate.CrackTimeDisplay);
    }

    [Fact]
    public void ShortLowercaseOnly_IsVeryWeak()
    {
        var estimate = PasswordStrengthEstimator.Estimate("abc");

        Assert.Equal(PasswordStrengthLevel.VeryWeak, estimate.Level);
        Assert.Equal(1, estimate.FilledSegments);
    }

    [Fact]
    public void EightLowercaseChars_IsFair()
    {
        var estimate = PasswordStrengthEstimator.Estimate("asdfasdf");

        Assert.Equal(PasswordStrengthLevel.Fair, estimate.Level);
        Assert.Equal(3, estimate.FilledSegments);
        Assert.Contains("second", estimate.CrackTimeDisplay);
    }

    [Fact]
    public void LongMixedCharacterPassword_IsStrong()
    {
        var estimate = PasswordStrengthEstimator.Estimate("Tr0ub4dor&3xtraLong!");

        Assert.Equal(PasswordStrengthLevel.Strong, estimate.Level);
        Assert.Equal(4, estimate.FilledSegments);
        Assert.Equal("centuries", estimate.CrackTimeDisplay);
    }

    [Fact]
    public void FilledSegments_NeverExceedsFour()
    {
        var estimate = PasswordStrengthEstimator.Estimate(new string('X', 200) + "!1aA");

        Assert.InRange(estimate.FilledSegments, 0, 4);
    }

    [Fact]
    public void SingleCharacter_IsVeryWeakAndInstant()
    {
        var estimate = PasswordStrengthEstimator.Estimate("a");

        Assert.Equal(PasswordStrengthLevel.VeryWeak, estimate.Level);
        Assert.Equal("instantly", estimate.CrackTimeDisplay);
    }
}
