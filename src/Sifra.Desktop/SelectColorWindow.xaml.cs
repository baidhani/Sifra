using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sifra.Desktop;

/// <summary>Just the background-color half of the icon customization menu — swaps the credential to a plain colored-initial-letter icon.</summary>
public partial class SelectColorWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly string[] Palette =
    [
        "#5B8DEF", "#E67E22", "#27AE60", "#E74C3C",
        "#9B59B6", "#F1C40F", "#1ABC9C", "#7F8C8D",
    ];

    private readonly List<Border> _swatches = new();

    public string SelectedColorHex { get; private set; }

    public SelectColorWindow(string? currentColorHex = null)
    {
        InitializeComponent();
        SelectedColorHex = currentColorHex ?? Palette[0];
        BuildSwatches();
    }

    private void BuildSwatches()
    {
        foreach (var hex in Palette)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            var swatch = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = brush,
                Margin = new Thickness(0, 0, 10, 0),
                BorderThickness = new Thickness(2),
                BorderBrush = hex == SelectedColorHex ? Brushes.White : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) => SelectSwatch(swatch, hex);
            _swatches.Add(swatch);
            ColorSwatchesPanel.Children.Add(swatch);
        }
    }

    private void SelectSwatch(Border swatch, string hex)
    {
        foreach (var s in _swatches)
        {
            s.BorderBrush = Brushes.Transparent;
        }

        swatch.BorderBrush = Brushes.White;
        SelectedColorHex = hex;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
