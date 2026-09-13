using System.Windows.Media;
using Sifra.Vault.Health;

namespace Sifra.Desktop;

/// <summary>Shared strength-meter color mapping for the detail pane and the Add/Edit fields list.</summary>
internal static class PasswordStrengthUi
{
    public static Brush ColorFor(PasswordStrengthLevel level)
    {
        var hex = level switch
        {
            PasswordStrengthLevel.VeryWeak => "#E5484D",
            PasswordStrengthLevel.Weak => "#F5A623",
            PasswordStrengthLevel.Fair => "#F5D90A",
            _ => "#3DD68C",
        };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
