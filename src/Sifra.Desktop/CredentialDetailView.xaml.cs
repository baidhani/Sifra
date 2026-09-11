using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sifra.Vault.Credentials;

namespace Sifra.Desktop;

/// <summary>
/// Read-only display of one credential's full decrypted data. Editing
/// happens through AddCredentialWindow (opened via the Edit button here or
/// the toolbar) rather than inline, to avoid building a second, separate
/// live-editing surface for the same fields.
/// </summary>
public partial class CredentialDetailView : UserControl
{
    private readonly AppServices _services;
    private readonly string _vaultCredential;
    private readonly DispatcherTimer _otpTimer;
    private CredentialView? _current;
    private bool _passwordRevealed;
    private bool _pinRevealed;

    public event EventHandler? Changed;

    public CredentialDetailView(AppServices services, string vaultCredential)
    {
        InitializeComponent();
        _services = services;
        _vaultCredential = vaultCredential;

        _otpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _otpTimer.Tick += (_, _) => RefreshOtpFields();
        _otpTimer.Start();
    }

    public void ShowEmpty()
    {
        _current = null;
        EmptyStateText.Visibility = Visibility.Visible;
        DetailScroll.Visibility = Visibility.Collapsed;
    }

    public void ShowCredential(string credentialId)
    {
        _current = _services.Credentials.GetById(_vaultCredential, credentialId);
        _passwordRevealed = false;
        _pinRevealed = false;

        EmptyStateText.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;

        Render();
    }

    private void Render()
    {
        var c = _current;
        if (c is null) return;

        AvatarText.Text = c.Label.Length > 0 ? c.Label[..1].ToUpperInvariant() : "?";
        TitleText.Text = c.Label;
        HealthText.Text = $"Updated {c.UpdatedAtUtc:g}";
        UpdatedText.Text = $"Last updated: {c.UpdatedAtUtc:g}";

        FavoriteButton.Icon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.Star24,
            Foreground = c.IsFavorite
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B))
                : (System.Windows.Media.Brush)FindResource("Sifra.TextSecondaryBrush"),
        };

        UsernameText.Text = c.Username;

        RenderPassword();
        RenderPin();

        UrlPanel.Visibility = string.IsNullOrEmpty(c.Url) ? Visibility.Collapsed : Visibility.Visible;
        UrlLink.Content = c.Url;

        PhonePanel.Visibility = string.IsNullOrEmpty(c.Phone) ? Visibility.Collapsed : Visibility.Visible;
        PhoneText.Text = c.Phone;

        AccountNumberPanel.Visibility = string.IsNullOrEmpty(c.AccountNumber) ? Visibility.Collapsed : Visibility.Visible;
        AccountNumberText.Text = c.AccountNumber;

        PinPanel.Visibility = string.IsNullOrEmpty(c.Pin) ? Visibility.Collapsed : Visibility.Visible;

        NotesPanel.Visibility = string.IsNullOrEmpty(c.Notes) ? Visibility.Collapsed : Visibility.Visible;
        NotesText.Text = c.Notes;

        var labels = c.Labels ?? Array.Empty<string>();
        LabelsPanel.Visibility = labels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LabelsList.ItemsSource = labels;

        var customFields = c.CustomFields ?? Array.Empty<CustomFieldView>();
        CustomFieldsHeader.Visibility = customFields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CustomFieldsList.ItemsSource = customFields.Select(BuildCustomFieldRow).ToList();
    }

    private FrameworkElement BuildCustomFieldRow(CustomFieldView field)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Tag = field };
        var caption = new Wpf.Ui.Controls.TextBlock
        {
            Text = $"{field.Name} ({CustomFieldTypeDisplay.DisplayNameFor(field.Type)})",
            FontTypography = Wpf.Ui.Controls.FontTypography.Caption,
        };
        caption.SetResourceReference(Control.ForegroundProperty, "Sifra.TextSecondaryBrush");
        panel.Children.Add(caption);

        if (field.Type == CustomFieldType.OneTimePassword)
        {
            var otpText = new Wpf.Ui.Controls.TextBlock
            {
                Name = "OtpCode",
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
            };
            otpText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextPrimaryBrush");
            panel.Children.Add(otpText);
            UpdateOtpText(otpText, field.Value);
        }
        else
        {
            var isSensitive = field.Type is CustomFieldType.Password or CustomFieldType.Pin or CustomFieldType.Secret;
            var valueText = new Wpf.Ui.Controls.TextBlock
            {
                Text = isSensitive ? new string('•', 8) : field.Value,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            };
            valueText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextPrimaryBrush");

            if (isSensitive)
            {
                var revealed = false;
                valueText.MouseLeftButtonUp += (_, _) =>
                {
                    revealed = !revealed;
                    valueText.Text = revealed ? field.Value : new string('•', 8);
                };
                valueText.Cursor = System.Windows.Input.Cursors.Hand;
                valueText.ToolTip = "Click to show/hide";
            }

            panel.Children.Add(valueText);
        }

        return panel;
    }

    private void RefreshOtpFields()
    {
        if (_current?.CustomFields is null) return;

        foreach (var child in CustomFieldsList.Items)
        {
            if (child is StackPanel { Tag: CustomFieldView field } panel && field.Type == CustomFieldType.OneTimePassword)
            {
                if (panel.Children.Count > 1 && panel.Children[1] is Wpf.Ui.Controls.TextBlock otpText)
                {
                    UpdateOtpText(otpText, field.Value);
                }
            }
        }
    }

    private static void UpdateOtpText(Wpf.Ui.Controls.TextBlock textBlock, string base32Secret)
    {
        try
        {
            var code = TotpGenerator.GenerateCode(base32Secret);
            var remaining = TotpGenerator.SecondsRemaining();
            textBlock.Text = $"{code}  ({remaining}s)";
        }
        catch (FormatException)
        {
            textBlock.Text = "Invalid secret";
        }
    }

    private void RenderPassword()
    {
        if (_current is null) return;
        PasswordText.Text = _passwordRevealed ? _current.Password : new string('•', 10);
    }

    private void RenderPin()
    {
        if (_current is null) return;
        PinText.Text = _pinRevealed ? _current.Pin : new string('•', 4);
    }

    private void OnRevealPasswordClick(object sender, RoutedEventArgs e)
    {
        _passwordRevealed = !_passwordRevealed;
        RenderPassword();
    }

    private void OnRevealPinClick(object sender, RoutedEventArgs e)
    {
        _pinRevealed = !_pinRevealed;
        RenderPin();
    }

    private void OnCopyUsernameClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        _services.Credentials.CopyUsername(_vaultCredential, _current.Id);
    }

    private void OnCopyPasswordClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        _services.Credentials.CopyPassword(_vaultCredential, _current.Id);
    }

    private void OnOpenUrlClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_current?.Url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_current.Url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Failure path: an unreachable/invalid URL must not crash the app.
            MessageBox.Show(Window.GetWindow(this), "Could not open this URL.", "Sifra", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        _services.Credentials.SetFavorite(_current.Id, !_current.IsFavorite);
        ShowCredential(_current.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        var window = new AddCredentialWindow(_services, _vaultCredential, _current)
        {
            Owner = Window.GetWindow(this),
        };

        if (window.ShowDialog() == true)
        {
            ShowCredential(_current.Id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
