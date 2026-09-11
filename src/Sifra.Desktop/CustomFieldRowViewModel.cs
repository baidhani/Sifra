using Sifra.Vault.Credentials;

namespace Sifra.Desktop;

/// <summary>One row in the Add/Edit form's dynamic custom-fields list.</summary>
public sealed class CustomFieldRowViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public CustomFieldType Type { get; set; } = CustomFieldType.Text;
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
