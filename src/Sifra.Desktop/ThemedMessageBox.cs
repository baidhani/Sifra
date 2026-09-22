using System.Windows;

namespace Sifra.Desktop;

/// <summary>
/// Drop-in replacement for System.Windows.MessageBox that matches Sifra's
/// own dialog chrome (see ThemedMessageBoxWindow) instead of either the
/// native OS message box or WPF-UI's built-in MessageBox control — both of
/// which looked visually inconsistent with the rest of the app's themed
/// windows (AddCredentialWindow, SetTagsWindow, etc).
/// </summary>
public static class ThemedMessageBox
{
    /// <summary>Icon glyph + color shown next to the message — mirrors the subset of System.Windows.MessageBoxImage this app actually uses.</summary>
    public enum Icon
    {
        Information,
        Warning,
        Error,
    }

    /// <summary>OK-only informational/warning/error dialog — the ThemedMessageBox equivalent of MessageBox.Show(owner, message, title, MessageBoxButton.OK, image).</summary>
    public static void Show(Window? owner, string message, string title, Icon icon = Icon.Information)
    {
        var window = new ThemedMessageBoxWindow();
        window.Configure(title, message, icon, primaryText: "OK", secondaryText: null);
        if (owner is not null)
        {
            window.Owner = owner;
        }
        window.ShowDialog();
    }

    /// <summary>Yes/No confirmation dialog — returns true only if the user picked Yes. The ThemedMessageBox equivalent of MessageBox.Show(..., MessageBoxButton.YesNo, image) checked against MessageBoxResult.Yes.</summary>
    public static bool ShowConfirm(Window? owner, string message, string title, Icon icon = Icon.Warning)
    {
        var window = new ThemedMessageBoxWindow();
        window.Configure(title, message, icon, primaryText: "Yes", secondaryText: "Cancel");
        if (owner is not null)
        {
            window.Owner = owner;
        }
        return window.ShowDialog() == true;
    }
}
