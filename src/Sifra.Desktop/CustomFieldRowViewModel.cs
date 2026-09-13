using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Sifra.Vault.Credentials;
using Sifra.Vault.Health;

namespace Sifra.Desktop;

/// <summary>
/// One row in the Add/Edit form's dynamic custom-fields list. Implements
/// INotifyPropertyChanged so the live strength meter (mirrors the read-only
/// detail pane's — see CredentialDetailView.BuildStrengthMeter) updates as
/// the user types a Password-type value, not just after save.
/// </summary>
public sealed class CustomFieldRowViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _value = string.Empty;
    private CustomFieldType _type = CustomFieldType.Text;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            OnPropertyChanged(nameof(Value));
            RaiseStrengthChanged();
        }
    }

    public CustomFieldType Type
    {
        get => _type;
        set
        {
            _type = value;
            OnPropertyChanged(nameof(Type));
            RaiseStrengthChanged();
        }
    }

    public Visibility StrengthVisibility =>
        Type == CustomFieldType.Password && !string.IsNullOrEmpty(Value) ? Visibility.Visible : Visibility.Collapsed;

    // Matches Sifra.Theme.Dark's Border color (#24466F). Hardcoded rather
    // than a DynamicResource lookup because this view model has no
    // FrameworkElement to resolve resources through; acceptable since the
    // app is dark-theme-only in current use, but would need revisiting if
    // the light theme becomes reachable from here.
    private static readonly Brush InactiveSegmentBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x46, 0x6F));

    public Brush Segment1Brush => SegmentBrush(0);
    public Brush Segment2Brush => SegmentBrush(1);
    public Brush Segment3Brush => SegmentBrush(2);
    public Brush Segment4Brush => SegmentBrush(3);

    public string CrackTimeText => $"Crack time: {PasswordStrengthEstimator.Estimate(Value).CrackTimeDisplay}";

    private Brush SegmentBrush(int index)
    {
        var estimate = PasswordStrengthEstimator.Estimate(Value);
        return index < estimate.FilledSegments ? PasswordStrengthUi.ColorFor(estimate.Level) : InactiveSegmentBrush;
    }

    private void RaiseStrengthChanged()
    {
        OnPropertyChanged(nameof(StrengthVisibility));
        OnPropertyChanged(nameof(Segment1Brush));
        OnPropertyChanged(nameof(Segment2Brush));
        OnPropertyChanged(nameof(Segment3Brush));
        OnPropertyChanged(nameof(Segment4Brush));
        OnPropertyChanged(nameof(CrackTimeText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Maps each CustomFieldType to the label shown in the type picker, matching the reference "Add field" menu wording.</summary>
public static class CustomFieldTypeDisplay
{
    public static readonly IReadOnlyList<CustomFieldTypeOption> Options = new[]
    {
        new CustomFieldTypeOption(CustomFieldType.Text, "Text"),
        new CustomFieldTypeOption(CustomFieldType.Number, "Number"),
        new CustomFieldTypeOption(CustomFieldType.Login, "Login"),
        new CustomFieldTypeOption(CustomFieldType.Password, "Password"),
        new CustomFieldTypeOption(CustomFieldType.OneTimePassword, "One-time password (2FA)"),
        new CustomFieldTypeOption(CustomFieldType.Expiry, "Expiry"),
        new CustomFieldTypeOption(CustomFieldType.Website, "Website"),
        new CustomFieldTypeOption(CustomFieldType.Email, "Email"),
        new CustomFieldTypeOption(CustomFieldType.Phone, "Phone"),
        new CustomFieldTypeOption(CustomFieldType.Date, "Date"),
        new CustomFieldTypeOption(CustomFieldType.Pin, "PIN"),
        new CustomFieldTypeOption(CustomFieldType.Secret, "Secret"),
    };

    public static string DisplayNameFor(CustomFieldType type) =>
        Options.FirstOrDefault(o => o.Type == type)?.DisplayName ?? type.ToString();
}

public sealed record CustomFieldTypeOption(CustomFieldType Type, string DisplayName);
