namespace Sifra.Vault.Credentials;

public enum PasswordGeneratorMode
{
    Memorable,
    LettersAndNumbers,
    Random,
    NumbersOnly,
}

/// <summary>
/// Options for PasswordGeneratorService.Generate. Length means characters
/// for every mode except Memorable, where it means word count — the two
/// don't share a scale, so the UI is expected to swap the slider's range
/// and label depending on the selected mode.
/// </summary>
public sealed record PasswordGeneratorOptions(
    PasswordGeneratorMode Mode = PasswordGeneratorMode.Random,
    int Length = 12,
    string Symbols = "@#$&*-+!?^._:;\"'\\/{}[]=()<>",
    bool ExcludeSimilarCharacters = false,
    string Separator = "-");
