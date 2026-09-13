using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sifra.Desktop;

/// <summary>
/// Tabbed grid of built-in Fluent System Icons plus a background color
/// swatch row, matching the reference "Select Symbol" dialog's shape
/// (categories across the top, icon grid below) — see
/// CredentialIconSymbolCatalog for why these are generic icons rather than
/// brand logos.
/// </summary>
public partial class SelectSymbolWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly string[] Palette =
    [
        "#5B8DEF", "#E67E22", "#27AE60", "#E74C3C",
        "#9B59B6", "#F1C40F", "#1ABC9C", "#7F8C8D",
    ];

    private readonly List<Border> _colorSwatches = new();
    private readonly List<Border> _iconTiles = new();
    private string _category = CredentialIconSymbolCatalog.CategoryNames[0];

    public string? SelectedSymbolName { get; private set; }
    public string SelectedColorHex { get; private set; } = Palette[0];

    public SelectSymbolWindow(string? currentSymbolName = null, string? currentColorHex = null)
    {
        InitializeComponent();
        SelectedSymbolName = currentSymbolName;
        if (currentColorHex is not null)
        {
            SelectedColorHex = currentColorHex;
        }

        BuildCategoryTabs();
        BuildColorSwatches();
        ShowCategory(_category);
        UpdateOkEnabled();
    }

    private void BuildCategoryTabs()
    {
        foreach (var category in CredentialIconSymbolCatalog.CategoryNames)
        {
            var tab = new RadioButton
            {
                GroupName = "SymbolCategory",
                Content = category,
                Style = (Style)FindResource("CategoryTabButton"),
                IsChecked = category == _category,
            };
            tab.Checked += (_, _) => ShowCategory(category);
            CategoryTabsPanel.Children.Add(tab);
        }
    }

    private void ShowCategory(string category)
    {
        _category = category;
        IconGridPanel.Children.Clear();
        _iconTiles.Clear();

        foreach (var symbolName in CredentialIconSymbolCatalog.GetSymbols(category))
        {
            if (!Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(symbolName, out var symbol))
            {
                continue;
            }

            var tile = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(28),
                Background = (Brush)FindResource("Sifra.HoverBrush"),
                Margin = new Thickness(0, 0, 10, 10),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = symbolName,
                Child = new Wpf.Ui.Controls.SymbolIcon
                {
                    Symbol = symbol,
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            tile.MouseLeftButtonUp += (_, _) => SelectIconTile(tile, symbolName);
            if (symbolName == SelectedSymbolName)
            {
                tile.BorderBrush = (Brush)FindResource("Sifra.Brand.PrimaryBrush");
            }

            _iconTiles.Add(tile);
            IconGridPanel.Children.Add(tile);
        }
    }

    private void SelectIconTile(Border tile, string symbolName)
    {
        foreach (var t in _iconTiles)
        {
            t.BorderBrush = Brushes.Transparent;
        }

        tile.BorderBrush = (Brush)FindResource("Sifra.Brand.PrimaryBrush");
        SelectedSymbolName = symbolName;
        UpdateOkEnabled();
    }

    private void BuildColorSwatches()
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
                Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(2),
                BorderBrush = hex == SelectedColorHex ? Brushes.White : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) => SelectSwatch(swatch, hex);
            _colorSwatches.Add(swatch);
            ColorSwatchesPanel.Children.Add(swatch);
        }
    }

    private void SelectSwatch(Border swatch, string hex)
    {
        foreach (var s in _colorSwatches)
        {
            s.BorderBrush = Brushes.Transparent;
        }

        swatch.BorderBrush = Brushes.White;
        SelectedColorHex = hex;
    }

    private void UpdateOkEnabled()
    {
        OkButton.IsEnabled = SelectedSymbolName is not null;
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
