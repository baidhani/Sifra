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

        var brush = icon switch
        {
            ThemedMessageBox.Icon.Warning => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            ThemedMessageBox.Icon.Error => new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
            _ => new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF)),
        };

        InfoIcon.Visibility = Visibility.Collapsed;
        WarningIcon.Visibility = Visibility.Collapsed;
        ErrorIcon.Visibility = Visibility.Collapsed;

        switch (icon)
        {
            case ThemedMessageBox.Icon.Warning:
                WarningIcon.Visibility = Visibility.Visible;
                WarningTriangle.Stroke = brush;
                WarningLine.Stroke = brush;
                WarningDot.Fill = brush;
                break;
            case ThemedMessageBox.Icon.Error:
                ErrorIcon.Visibility = Visibility.Visible;
                ErrorCircle.Stroke = brush;
                ErrorX1.Stroke = brush;
                ErrorX2.Stroke = brush;
                break;
            default:
                InfoIcon.Visibility = Visibility.Visible;
                InfoCircle.Stroke = brush;
                InfoLine.Stroke = brush;
                InfoDot.Fill = brush;
                break;
        }

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
