using System.Windows;
using System.Windows.Controls;

namespace Sifra.Desktop;

/// <summary>
/// Shows previous values of a single Password-type field (newest first),
/// letting the user copy an old value back out or clear the history
/// entirely. Read-only otherwise — history entries are never restored
/// automatically, only copied, so restoring one is a deliberate Edit +
/// paste rather than a silent overwrite.
/// </summary>
public partial class PasswordHistoryWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppServices _services;
    private readonly string _vaultCredential;
    private readonly string _credentialId;
    private readonly string _fieldName;
    private string? _selectedValue;

    public PasswordHistoryWindow(AppServices services, string vaultCredential, string credentialId, string fieldName)
    {
        InitializeComponent();
        _services = services;
        _vaultCredential = vaultCredential;
        _credentialId = credentialId;
        _fieldName = fieldName;

        FieldNameText.Text = fieldName;
        RefreshEntries();
    }

    private void RefreshEntries()
    {
        EntriesPanel.Children.Clear();
        _selectedValue = null;
        CopyButton.IsEnabled = false;

        var entries = _services.PasswordHistory.List(_vaultCredential, _credentialId, _fieldName);
        EmptyStateText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var entry in entries)
        {
            var content = new StackPanel();
            var valueText = new TextBlock { Text = entry.Value, FontSize = 15 };
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "Sifra.TextPrimaryBrush");
            content.Children.Add(valueText);
            var dateText = new TextBlock { Text = $"Changed {entry.ChangedAtUtc:g}", FontSize = 12 };
            dateText.SetResourceReference(TextBlock.ForegroundProperty, "Sifra.TextSecondaryBrush");
            content.Children.Add(dateText);

            var radio = new RadioButton
            {
                GroupName = "PasswordHistoryEntry",
                Content = content,
                Margin = new Thickness(0, 6, 0, 6),
            };
            radio.SetResourceReference(Control.ForegroundProperty, "Sifra.TextPrimaryBrush");
            radio.Checked += (_, _) =>
            {
                _selectedValue = entry.Value;
                CopyButton.IsEnabled = true;
            };
            EntriesPanel.Children.Add(radio);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _services.PasswordHistory.Clear(_credentialId, _fieldName);
        RefreshEntries();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_selectedValue is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_selectedValue);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Failure path: another process briefly holding the clipboard must not crash the app.
            MessageBox.Show(this, "Could not copy to the clipboard.", "Sifra", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
