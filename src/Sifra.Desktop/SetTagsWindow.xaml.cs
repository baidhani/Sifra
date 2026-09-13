using System.Windows;
using System.Windows.Media;
using Sifra.Vault.Tags;

namespace Sifra.Desktop;

/// <summary>Row view model backing one checkbox in the Set Tags list.</summary>
public sealed class TagCheckboxRow
{
    public required string Name { get; init; }
    public required SolidColorBrush ColorBrush { get; init; }
    public bool IsChecked { get; set; }
}

/// <summary>
/// Lets the user pick which of the vault's shared tags apply to the
/// credential being edited, and create new ones on the fly — matching the
/// reference "Set Labels" dialog (tags were originally called labels —
/// renamed for clarity, no functional change). Selection only takes effect
/// if the user clicks OK; the underlying credential is not touched here,
/// the caller reads SelectedTags back out.
/// </summary>
public partial class SetTagsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly TagService _tagService;
    private readonly HashSet<string> _selected;
    private List<TagCheckboxRow> _rows = new();

    public IReadOnlyList<string> SelectedTags { get; private set; } = Array.Empty<string>();

    public SetTagsWindow(TagService tagService, IReadOnlyList<string> currentTags)
    {
        InitializeComponent();
        _tagService = tagService;
        _selected = new HashSet<string>(currentTags, StringComparer.OrdinalIgnoreCase);
        RefreshList();
    }

    private void RefreshList()
    {
        var tags = _tagService.List();
        _rows = tags.Select(t => new TagCheckboxRow
        {
            Name = t.Name,
            ColorBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(t.Color)!,
            IsChecked = _selected.Contains(t.Name),
        }).ToList();

        TagsList.ItemsSource = _rows;
        EmptyStateText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddTagClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddTagWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _tagService.Add(dialog.TagName, dialog.TagColor, dialog.PinToTop);
            _selected.Add(dialog.TagName);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Sifra", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshList();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        SelectedTags = _rows.Where(r => r.IsChecked).Select(r => r.Name).ToList();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
