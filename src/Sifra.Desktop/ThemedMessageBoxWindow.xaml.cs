using System.Windows;
using System.Windows.Media;

namespace Sifra.Desktop;

/// <summary>
/// Backing code for the themed message box window — styled to match the
/// app's own dialogs (AddCredentialWindow, SetTagsWindow: TitleBar +
/// card + right-aligned button row) rather than WPF-UI's built-in
/// MessageBox control, whose look didn't match Sifra's own window chrome.
/// </summary>
public partial class ThemedMessageBoxWindow : Wpf.Ui.Controls.FluentWindow
{
    public ThemedMessageBoxWindow()
    {
        InitializeComponent();
        // ResizeMode="NoResize" alone doesn't stop edge-drag resizing on a
        // FluentWindow — see NonResizableWindowGuard's remarks.
        NonResizableWindowGuard.Apply(this);
    }

    public void Configure(string title, string message, ThemedMessageBox.Icon icon, string primaryText, string? secondaryText)
    {
        Title = title;
        MessageText.Text = message;

        var (symbol, brush) = icon switch
        {
            ThemedMessageBox.Icon.Warning => (Wpf.Ui.Controls.SymbolRegular.Warning24, new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))),
            ThemedMessageBox.Icon.Error => (Wpf.Ui.Controls.SymbolRegular.ErrorCircle24, new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D))),
            _ => (Wpf.Ui.Controls.SymbolRegular.Info24, new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF))),
        };
        MessageIcon.Symbol = symbol;
        MessageIcon.Foreground = brush;

        PrimaryButton.Content = primaryText;

        if (secondaryText is null)
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
        }
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
