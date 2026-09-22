using System.Windows;
using Sifra.Vault.Credentials;
using Wpf.Ui.Controls;

namespace Sifra.Desktop;

/// <summary>
/// The "gear" sub-dialog from Generate Password — mode-specific extras
/// (Memorable's separator, Random's symbol set and similar-character
/// exclusion) that don't fit on the main dialog without crowding it.
/// Close applies whatever is currently in the fields; there is no
/// separate Cancel, matching the reference design (Close/Restore only).
/// </summary>
public partial class GeneratorOptionsWindow : FluentWindow
{
    private static readonly PasswordGeneratorOptions Defaults = new();

    public string Separator { get; private set; }
    public string Symbols { get; private set; }
    public bool ExcludeSimilarCharacters { get; private set; }

    public GeneratorOptionsWindow(string separator, string symbols, bool excludeSimilarCharacters)
    {
        InitializeComponent();
        Separator = separator;
        Symbols = symbols;
        ExcludeSimilarCharacters = excludeSimilarCharacters;

        SeparatorBox.Text = separator;
        SymbolsBox.Text = symbols;
        ExcludeSimilarCheckBox.IsChecked = excludeSimilarCharacters;
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        SeparatorBox.Text = Defaults.Separator;
        SymbolsBox.Text = Defaults.Symbols;
        ExcludeSimilarCheckBox.IsChecked = Defaults.ExcludeSimilarCharacters;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Separator = string.IsNullOrEmpty(SeparatorBox.Text) ? Defaults.Separator : SeparatorBox.Text;
        Symbols = SymbolsBox.Text;
        ExcludeSimilarCharacters = ExcludeSimilarCheckBox.IsChecked == true;
        DialogResult = true;
    }
}
