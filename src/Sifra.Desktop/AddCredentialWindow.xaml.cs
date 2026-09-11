using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Sifra.Vault.Credentials;

namespace Sifra.Desktop;

/// <summary>
/// Same form serves both Add and Edit. Add shows secrets (password, PIN)
/// in plain text while typing (you're actively choosing them and want to
/// confirm what you typed); Edit shows them masked with a reveal toggle
/// (existing secrets, default-hidden like everywhere else in the app).
/// Custom field values follow the same "plain while editing" rule
/// regardless of type — masking only matters once a field is being
/// viewed, not while you're actively typing it in.
/// </summary>
public partial class AddCredentialWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppServices _services;
    private readonly string _vaultCredential;
    private readonly string? _editingCredentialId;
    private readonly ObservableCollection<CustomFieldRowViewModel> _customFields = new();

    public AddCredentialWindow(AppServices services, string vaultCredential)
    {
        InitializeComponent();
        _services = services;
        _vaultCredential = vaultCredential;
        CustomFieldsList.ItemsSource = _customFields;
    }

    public AddCredentialWindow(AppServices services, string vaultCredential, CredentialView existing)
        : this(services, vaultCredential)
    {
        _editingCredentialId = existing.Id;
        Title = "Edit Credential";
        LabelBox.Text = existing.Label;
        UsernameBox.Text = existing.Username;
        UrlBox.Text = existing.Url ?? string.Empty;
        PhoneBox.Text = existing.Phone ?? string.Empty;
        AccountNumberBox.Text = existing.AccountNumber ?? string.Empty;
        NotesBox.Text = existing.Notes ?? string.Empty;
        LabelsBox.Text = existing.Labels is { Count: > 0 } ? string.Join(", ", existing.Labels) : string.Empty;
        FavoriteCheckBox.IsChecked = existing.IsFavorite;

        NewPasswordBox.Visibility = Visibility.Collapsed;
        NewPasswordHint.Visibility = Visibility.Collapsed;
        ExistingPasswordBox.Visibility = Visibility.Visible;
        ExistingPasswordBox.Password = existing.Password ?? string.Empty;

        NewPinBox.Visibility = Visibility.Collapsed;
        NewPinHint.Visibility = Visibility.Collapsed;
        ExistingPinBox.Visibility = Visibility.Visible;
        ExistingPinBox.Password = existing.Pin ?? string.Empty;

        foreach (var field in existing.CustomFields ?? Array.Empty<CustomFieldView>())
        {
            _customFields.Add(new CustomFieldRowViewModel { Name = field.Name, Value = field.Value, Type = field.Type });
        }
    }

    private string EnteredPassword => _editingCredentialId is null ? NewPasswordBox.Text : ExistingPasswordBox.Password;
    private string EnteredPin => _editingCredentialId is null ? NewPinBox.Text : ExistingPinBox.Password;

    private void OnAddCustomFieldClick(object sender, RoutedEventArgs e)
    {
        _customFields.Add(new CustomFieldRowViewModel());
    }

    private void OnRemoveCustomFieldClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is CustomFieldRowViewModel row)
        {
            _customFields.Remove(row);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var label = LabelBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = EnteredPassword;
        var url = string.IsNullOrWhiteSpace(UrlBox.Text) ? null : UrlBox.Text.Trim();
        var phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
        var accountNumber = string.IsNullOrWhiteSpace(AccountNumberBox.Text) ? null : AccountNumberBox.Text.Trim();
        var pin = string.IsNullOrWhiteSpace(EnteredPin) ? null : EnteredPin;
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        var isFavorite = FavoriteCheckBox.IsChecked == true;
        var labels = LabelsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0)
            .ToList();
        var customFields = _customFields
            .Where(f => f.Name.Trim().Length > 0)
            .Select(f => (f.Name.Trim(), f.Value, f.Type))
            .ToList();

        if (label.Length == 0 || username.Length == 0 || password.Length == 0)
        {
            ErrorText.Text = "Label, username, and password are all required.";
            return;
        }

        if (_editingCredentialId is null)
        {
            _services.Credentials.Add(_vaultCredential, label, username, password, url,
                phone, notes, accountNumber, pin, customFields, isFavorite, labels);
        }
        else
        {
            _services.Credentials.Edit(_vaultCredential, _editingCredentialId, label, username, password, url,
                phone, notes, accountNumber, pin, customFields, isFavorite, labels);
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
