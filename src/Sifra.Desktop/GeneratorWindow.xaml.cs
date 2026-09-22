using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sifra.Vault.Credentials;
using Sifra.Vault.Health;
using Wpf.Ui.Controls;

namespace Sifra.Desktop;

/// <summary>Standalone password generator tool — not tied to any specific credential field, matching the toolbar's "Generator" button.</summary>
public partial class GeneratorWindow : FluentWindow
{
    private const int MinMemorableWords = 3;
    private const int MaxMemorableWords = 12;
    private const int MinCharacterLength = 4;
    private const int MaxCharacterLength = 64;

    private string _separator = "-";
    private string _symbols = new PasswordGeneratorOptions().Symbols;
    private bool _excludeSimilarCharacters;
    private bool _initialized;

    public GeneratorWindow()
    {
        InitializeComponent();
        LengthSlider.Value = 12;
        _initialized = true;
        Regenerate();
    }

    private PasswordGeneratorMode SelectedMode =>
        MemorableRadio.IsChecked == true ? PasswordGeneratorMode.Memorable
        : LettersAndNumbersRadio.IsChecked == true ? PasswordGeneratorMode.LettersAndNumbers
        : NumbersOnlyRadio.IsChecked == true ? PasswordGeneratorMode.NumbersOnly
        : PasswordGeneratorMode.Random;

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        // Length means words for Memorable and characters for everything
        // else — two different scales, so the slider's range and label
        // swap with the mode rather than sharing one meaning.
        var isMemorable = SelectedMode == PasswordGeneratorMode.Memorable;
        LengthSlider.Minimum = isMemorable ? MinMemorableWords : MinCharacterLength;
        LengthSlider.Maximum = isMemorable ? MaxMemorableWords : MaxCharacterLength;
        LengthSlider.Value = Math.Clamp(LengthSlider.Value, LengthSlider.Minimum, LengthSlider.Maximum);

        Regenerate();
    }

    private void OnLengthSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
        {
            return;
        }

        Regenerate();
    }

    private void OnRegenerateClick(object sender, RoutedEventArgs e) => Regenerate();

    private void Regenerate()
    {
        ErrorText.Text = string.Empty;
        var isMemorable = SelectedMode == PasswordGeneratorMode.Memorable;
        var length = (int)LengthSlider.Value;
        LengthText.Text = isMemorable ? $"Words: {length}" : $"Length: {length}";

        try
        {
            var options = new PasswordGeneratorOptions(SelectedMode, length, _symbols, _excludeSimilarCharacters, _separator);
            var password = PasswordGeneratorService.Generate(options);
            PasswordText.Text = password;
            UpdateStrengthMeter(password);
        }
        catch (ArgumentException ex)
        {
            ErrorText.Text = ex.Message;
            PasswordText.Text = string.Empty;
        }
    }

    private void UpdateStrengthMeter(string password)
    {
        var estimate = PasswordStrengthEstimator.Estimate(password);
        var filled = PasswordStrengthUi.ColorFor(estimate.Level);
        var empty = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));

        var segments = new[] { Segment1, Segment2, Segment3, Segment4 };
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i].Background = i < estimate.FilledSegments ? filled : empty;
        }

        CrackTimeText.Text = $"Crack time: {estimate.CrackTimeDisplay}";
    }

    private void OnOptionsClick(object sender, RoutedEventArgs e)
    {
        var window = new GeneratorOptionsWindow(_separator, _symbols, _excludeSimilarCharacters) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _separator = window.Separator;
            _symbols = window.Symbols;
            _excludeSimilarCharacters = window.ExcludeSimilarCharacters;
            Regenerate();
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(PasswordText.Text))
        {
            Clipboard.SetText(PasswordText.Text);
        }
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
