using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sifra.Desktop;

/// <summary>
/// Name + color + pin-to-top for a new shared tag, matching the
/// reference "Add Label" dialog (tags were originally called labels —
/// renamed for clarity, no functional change). Color is picked from a
/// small fixed palette (simple swatches) rather than a full color
/// picker — enough to visually distinguish tags without the extra UI weight.
/// </summary>
public partial class AddTagWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly string[] Palette =
    [
        "#5B8DEF", "#E67E22", "#27AE60", "#E74C3C",
        "#9B59B6", "#F1C40F", "#1ABC9C", "#7F8C8D",
    ];

    private readonly List<Border> _swatches = new();

    public string TagName { get; private set; } = string.Empty;
    public string TagColor { get; private set; } = Palette[0];
    public bool PinToTop { get; private set; }

    /// <param name="existing">
    /// Null for the normal "create a new tag" flow. Passing an existing
    /// tag switches the window into edit mode (title becomes "Edit Tag",
    /// every field prefilled with its current values) — reused by both the
    /// tag context menu's Rename and Select Color actions rather than
    /// building two near-identical small dialogs.
    /// </param>
    public AddTagWindow(Sifra.Vault.Tags.TagDefinition? existing = null)
    {
        InitializeComponent();
        BuildSwatches();
        NameBox.TextChanged += (_, _) => UpdateOkEnabled();

        if (existing is not null)
        {
            Title = "Edit Tag";
            NameBox.Text = existing.Name;
            PinToTopCheckBox.IsChecked = existing.PinnedToTop;
            var matchingSwatch = _swatches.FirstOrDefault(s => string.Equals((string)s.Tag, existing.Color, StringComparison.OrdinalIgnoreCase));
            if (matchingSwatch is not null)
            {
                SelectSwatch(matchingSwatch, existing.Color);
            }
        }

        UpdateOkEnabled();
    }

    private void BuildSwatches()
    {
        foreach (var hex in Palette)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = brush,
                Margin = new Thickness(0, 0, 8, 8),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) => SelectSwatch(swatch, hex);
            _swatches.Add(swatch);
            ColorSwatchesPanel.Children.Add(swatch);
        }

        SelectSwatch(_swatches[0], Palette[0]);
    }

    private void SelectSwatch(Border swatch, string hex)
    {
        foreach (var s in _swatches)
        {
            s.BorderBrush = Brushes.Transparent;
        }

        swatch.BorderBrush = Brushes.White;
        TagColor = hex;
    }

    private void UpdateOkEnabled()
    {
        OkButton.IsEnabled = NameBox.Text.Trim().Length > 0;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        TagName = NameBox.Text.Trim();
        PinToTop = PinToTopCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
