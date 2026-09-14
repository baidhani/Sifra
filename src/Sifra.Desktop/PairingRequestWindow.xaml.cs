using System.Windows;
using Wpf.Ui.Controls;

namespace Sifra.Desktop;

/// <summary>
/// The human approval step for extension pairing (Phase 2). Shown when
/// ExtensionPairingServer receives a pairing request over the named pipe.
/// Topmost + CenterScreen (not CenterOwner) since it can pop up while the
/// user is doing something else entirely — a browser tab, not necessarily
/// looking at Sifra.Desktop at all.
/// </summary>
public partial class PairingRequestWindow : FluentWindow
{
    public PairingRequestWindow(string browserName)
    {
        InitializeComponent();
        BrowserNameText.Text = browserName;
    }

    private void OnApproveClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnDenyClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
