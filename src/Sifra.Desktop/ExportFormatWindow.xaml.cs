using System.Windows;
using Sifra.Vault.Credentials;

namespace Sifra.Desktop;

/// <summary>Format-selection dialog for the tag context menu's "Export..." action — matches the reference "Export As" (XML/TXT/CSV) design.</summary>
public partial class ExportFormatWindow : Wpf.Ui.Controls.FluentWindow
{
    public CredentialExportFormat SelectedFormat { get; private set; } = CredentialExportFormat.PlainText;

    public ExportFormatWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        SelectedFormat = XmlOption.IsChecked == true ? CredentialExportFormat.Xml
            : CsvOption.IsChecked == true ? CredentialExportFormat.Csv
            : CredentialExportFormat.PlainText;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
