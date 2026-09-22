using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Sifra.Vault.Attachments;
using Sifra.Vault.Credentials;

namespace Sifra.Desktop;

/// <summary>
/// Same form serves both Add and Edit, across four tabs matching the
/// reference design: Fields (fully dynamic — see CustomFieldRowViewModel),
/// Notes (a single dedicated free-text field, stored under the hood as a
/// field literally named "Notes" so the backend model stays unified, but
/// never shown in the Fields tab list), Images, and Files. Images/Files
/// need a real credential id to attach to, so they only become usable once
/// the credential has been saved at least once — a brand-new credential
/// shows a "save first" hint on those two tabs instead.
/// </summary>
public partial class AddCredentialWindow : Wpf.Ui.Controls.FluentWindow
{
    private const string NotesFieldName = "Notes";

    private readonly AppServices _services;
    private readonly string _vaultCredential;
    private readonly string? _editingCredentialId;
    private readonly ObservableCollection<CustomFieldRowViewModel> _fields = new();
    private List<string> _selectedTags = new();

    public AddCredentialWindow(AppServices services, string vaultCredential)
    {
        InitializeComponent();
        _services = services;
        _vaultCredential = vaultCredential;
        FieldsList.ItemsSource = _fields;

        // A brand-new credential starts with the three most common fields,
        // matching the reference design — purely a convenience seed, all
        // three (and anything added after) are freely removable.
        _fields.Add(new CustomFieldRowViewModel { Name = "Login", Type = CustomFieldType.Login });
        _fields.Add(new CustomFieldRowViewModel { Name = "Password", Type = CustomFieldType.Password });
        _fields.Add(new CustomFieldRowViewModel { Name = "Website", Type = CustomFieldType.Website });

        // Set after InitializeComponent, not via XAML IsChecked="True" —
        // XAML's IsChecked raises Checked synchronously during parsing,
        // before later-declared elements like NotesTabPanel/ImagesTabPanel/
        // FilesTabPanel exist yet, which crashed OnTabChanged() with a
        // NullReferenceException (same class of bug as VaultView's
        // AllItemsCategory, fixed the same way).
        FieldsTabButton.IsChecked = true;

        ImagesUnsavedHint.Visibility = Visibility.Visible;
        ImagesReadyPanel.Visibility = Visibility.Collapsed;
        FilesUnsavedHint.Visibility = Visibility.Visible;
        FilesReadyPanel.Visibility = Visibility.Collapsed;
        UpdateTagsSummary();
    }

    public AddCredentialWindow(AppServices services, string vaultCredential, CredentialView existing)
        : this(services, vaultCredential)
    {
        _editingCredentialId = existing.Id;
        Title = "Edit Credential";
        LabelBox.Text = existing.Label;
        _selectedTags = existing.Tags?.ToList() ?? new List<string>();
        UpdateTagsSummary();
        FavoriteCheckBox.IsChecked = existing.IsFavorite;

        // Replace the seeded default fields with the credential's real
        // ones, pulling the "Notes" field (if any) out into its own tab
        // rather than showing it as a Fields-tab row.
        _fields.Clear();
        foreach (var field in existing.Fields)
        {
            if (string.Equals(field.Name, NotesFieldName, StringComparison.OrdinalIgnoreCase))
            {
                NotesBox.Text = field.Value;
                continue;
            }

            _fields.Add(new CustomFieldRowViewModel { Name = field.Name, Value = field.Value, Type = field.Type });
        }

        ImagesUnsavedHint.Visibility = Visibility.Collapsed;
        ImagesReadyPanel.Visibility = Visibility.Visible;
        FilesUnsavedHint.Visibility = Visibility.Collapsed;
        FilesReadyPanel.Visibility = Visibility.Visible;
        RefreshImages();
        RefreshFiles();
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        FieldsTabPanel.Visibility = FieldsTabButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        NotesTabPanel.Visibility = NotesTabButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ImagesTabPanel.Visibility = ImagesTabButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        FilesTabPanel.Visibility = FilesTabButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddFieldClick(object sender, RoutedEventArgs e)
    {
        _fields.Add(new CustomFieldRowViewModel());
    }

    private void OnRemoveFieldClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is CustomFieldRowViewModel row)
        {
            _fields.Remove(row);
        }
    }

    private void OnSetTagsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SetTagsWindow(_services.Tags, _selectedTags) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _selectedTags = dialog.SelectedTags.ToList();
            UpdateTagsSummary();
        }
    }

    private void UpdateTagsSummary()
    {
        TagsSummaryText.Text = _selectedTags.Count > 0 ? string.Join(", ", _selectedTags) : "No tags";
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var label = LabelBox.Text.Trim();
        var isFavorite = FavoriteCheckBox.IsChecked == true;
        var tags = _selectedTags;
        var fields = _fields
            .Where(f => f.Name.Trim().Length > 0)
            .Select(f => (f.Name.Trim(), f.Value, f.Type))
            .ToList();

        if (NotesBox.Text.Trim().Length > 0)
        {
            fields.Add((NotesFieldName, NotesBox.Text, CustomFieldType.Text));
        }

        if (label.Length == 0)
        {
            ErrorText.Text = "Title is required.";
            return;
        }

        string id;
        if (_editingCredentialId is null)
        {
            id = _services.Credentials.Add(_vaultCredential, label, fields, isFavorite, tags);
        }
        else
        {
            id = _editingCredentialId;
            _services.Credentials.Edit(_vaultCredential, id, label, fields, isFavorite, tags);
        }

        await AutoFetchWebsiteIconIfNeededAsync(id, fields);

        DialogResult = true;
    }

    // Auto-populates the icon from the Website field on save — but only
    // when nothing has been set yet. A manually chosen icon (symbol,
    // color, custom image, or a previously fetched favicon) always wins
    // and is never silently overwritten by this.
    private async Task AutoFetchWebsiteIconIfNeededAsync(string credentialId, List<(string Name, string Value, CustomFieldType Type)> fields)
    {
        var existingIcon = _services.Credentials.GetById(_vaultCredential, credentialId).Icon;
        if (existingIcon is not null)
        {
            return;
        }

        var websiteUrl = fields.FirstOrDefault(f => f.Type == CustomFieldType.Website).Value;
        if (string.IsNullOrWhiteSpace(websiteUrl))
        {
            return;
        }

        await _services.CredentialIcons.SetFromWebsiteAsync(credentialId, websiteUrl, CancellationToken.None);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnAddImageClick(object sender, RoutedEventArgs e)
    {
        if (_editingCredentialId is null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        AddAttachment(dialog.FileName, AttachmentKind.Image);
        RefreshImages();
    }

    private void OnAddFileClick(object sender, RoutedEventArgs e)
    {
        if (_editingCredentialId is null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Add file" };
        if (dialog.ShowDialog(this) != true) return;

        AddAttachment(dialog.FileName, AttachmentKind.File);
        RefreshFiles();
    }

    private void AddAttachment(string filePath, AttachmentKind kind)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            _services.Attachments.Add(_vaultCredential, _editingCredentialId!, Path.GetFileName(filePath), kind, bytes);
        }
        catch (IOException)
        {
            // Failure path: an unreadable source file (deleted/locked between
            // picking it and reading it) must not crash the dialog.
            ThemedMessageBox.Show(this, "Could not read that file.", "Sifra", ThemedMessageBox.Icon.Warning);
        }
    }

    private void RefreshImages()
    {
        if (_editingCredentialId is null) return;

        var thumbnails = new List<FrameworkElement>();
        foreach (var attachment in _services.Attachments.List(_editingCredentialId).Where(a => a.Kind == AttachmentKind.Image))
        {
            thumbnails.Add(BuildImageThumbnail(attachment));
        }
        ImagesList.ItemsSource = thumbnails;
    }

    private FrameworkElement BuildImageThumbnail(CredentialAttachment attachment)
    {
        var container = new Grid { Width = 100, Height = 100, Margin = new Thickness(0, 0, 10, 10) };

        try
        {
            var bytes = _services.Attachments.GetDecryptedBytes(_vaultCredential, attachment.Id);
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            var image = new System.Windows.Controls.Image
            {
                Source = bitmap,
                Stretch = System.Windows.Media.Stretch.UniformToFill,
                ToolTip = attachment.FileName,
            };
            container.Children.Add(image);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
        {
            // Failure path: a corrupted/unrecognized image must not crash the dialog.
            container.Children.Add(new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }

        var downloadButton = new Wpf.Ui.Controls.Button
        {
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowDownload24 },
            ToolTip = "Save",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(4),
        };
        downloadButton.Click += (_, _) => SaveFile(attachment);
        container.Children.Add(downloadButton);

        var removeButton = new Wpf.Ui.Controls.Button
        {
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Delete24 },
            ToolTip = "Remove this image",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(4),
        };
        removeButton.Click += (_, _) =>
        {
            _services.Attachments.Delete(attachment.Id);
            RefreshImages();
        };
        container.Children.Add(removeButton);

        return container;
    }

    private void RefreshFiles()
    {
        if (_editingCredentialId is null) return;

        var rows = new List<FrameworkElement>();
        foreach (var attachment in _services.Attachments.List(_editingCredentialId).Where(a => a.Kind == AttachmentKind.File))
        {
            rows.Add(BuildFileRow(attachment));
        }
        FilesList.ItemsSource = rows;
    }

    private FrameworkElement BuildFileRow(CredentialAttachment attachment)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new Wpf.Ui.Controls.TextBlock
        {
            Text = $"{attachment.FileName}  ({FormatSize(attachment.SizeBytes)})",
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextPrimaryBrush");
        Grid.SetColumn(nameText, 0);
        row.Children.Add(nameText);

        var saveButton = new Wpf.Ui.Controls.Button
        {
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowDownload24 },
            ToolTip = "Save",
            Margin = new Thickness(8, 0, 0, 0),
        };
        saveButton.Click += (_, _) => SaveFile(attachment);
        Grid.SetColumn(saveButton, 1);
        row.Children.Add(saveButton);

        var removeButton = new Wpf.Ui.Controls.Button
        {
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Delete24 },
            ToolTip = "Remove this file",
            Margin = new Thickness(8, 0, 0, 0),
        };
        removeButton.Click += (_, _) =>
        {
            _services.Attachments.Delete(attachment.Id);
            RefreshFiles();
        };
        Grid.SetColumn(removeButton, 2);
        row.Children.Add(removeButton);

        return row;
    }

    // Attachments are never opened/executed in place — a stored file could be
    // of any type (script, executable, macro document), so the only supported
    // action is saving the decrypted bytes to a location the user picks.
    private void SaveFile(CredentialAttachment attachment)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = attachment.FileName };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var bytes = _services.Attachments.GetDecryptedBytes(_vaultCredential, attachment.Id);
            File.WriteAllBytes(dialog.FileName, bytes);
        }
        catch (IOException)
        {
            // Failure path: destination locked/unwritable must not crash the dialog.
            ThemedMessageBox.Show(this, "Could not save this file.", "Sifra", ThemedMessageBox.Icon.Warning);
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
