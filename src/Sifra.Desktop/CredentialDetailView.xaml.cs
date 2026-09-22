using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sifra.Vault.Attachments;
using Sifra.Vault.Credentials;
using Sifra.Vault.Health;

namespace Sifra.Desktop;

/// <summary>Row view model backing one tag chip in the detail pane's Tags section.</summary>
public sealed class TagChipViewModel
{
    public required string Name { get; init; }
    public required System.Windows.Media.Brush ColorBrush { get; init; }
}

/// <summary>
/// Read-only display of one credential's full decrypted data. Editing
/// happens through AddCredentialWindow (opened via the Edit button here or
/// the toolbar) rather than inline, to avoid building a second, separate
/// live-editing surface for the same fields.
///
/// Fully dynamic field model: there is no fixed Username/Password/Url/
/// Phone/AccountNumber/Pin/Notes shape any more, so every field (whatever
/// its name or type) renders through the same BuildFieldRow logic below —
/// masked with click-to-reveal for Password/Pin/Secret, a live TOTP code
/// for OneTimePassword, a clickable link for Website, plain text otherwise,
/// and a copy button on every row.
/// </summary>
public partial class CredentialDetailView : UserControl
{
    private readonly AppServices _services;
    private readonly string _vaultCredential;
    private readonly DispatcherTimer _otpTimer;
    private CredentialView? _current;
    private string _searchQuery = string.Empty;

    public event EventHandler? Changed;

    /// <summary>
    /// Set by VaultView right after construction — the Lock guard's real
    /// logic (session-unlock tracking, the master-password prompt) lives
    /// there, since it's shared with the toolbar/context-menu paths. Takes
    /// (id, label, isLocked), returns whether modification may proceed.
    /// Null means "no guard wired up" (e.g. an isolated test), which
    /// allows everything rather than silently blocking.
    /// </summary>
    public Func<string, string, bool, bool>? EnsureUnlockedForModification { get; set; }

    /// <summary>
    /// Set by VaultView right after construction — reports whether the given
    /// credential id is currently session-unlocked, so Render() can pick the
    /// open- vs closed-padlock glyph next to the title. Null (e.g. an
    /// isolated test) is treated as "never session-unlocked".
    /// </summary>
    public Func<string, bool>? IsSessionUnlocked { get; set; }

    /// <summary>
    /// Set by VaultView as the search box changes, so whichever credential
    /// is currently shown re-highlights live — mirrors CredentialRow's
    /// SearchQuery for the list, but applied via TextBlock.Inlines here
    /// since these fields are built in code, not data-bound.
    /// </summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value)
            {
                return;
            }
            _searchQuery = value;
            if (_current is not null)
            {
                Render();
            }
        }
    }

    /// <summary>
    /// Splits text into runs, highlighting whichever ones match SearchQuery
    /// — the detail-pane equivalent of CredentialRow.BuildSegments, just
    /// applied directly to Inlines since this view builds its fields in
    /// code rather than through data-bound templates.
    /// </summary>
    private void SetHighlightedText(TextBlock block, string text)
    {
        block.Inlines.Clear();
        if (_searchQuery.Length == 0 || text.Length == 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        var index = 0;
        while (index < text.Length)
        {
            var matchIndex = text.IndexOf(_searchQuery, index, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                block.Inlines.Add(new Run(text[index..]));
                break;
            }

            if (matchIndex > index)
            {
                block.Inlines.Add(new Run(text[index..matchIndex]));
            }
            block.Inlines.Add(new Run(text.Substring(matchIndex, _searchQuery.Length)) { Background = SearchHighlightBrush });
            index = matchIndex + _searchQuery.Length;
        }
    }

    // Same semi-transparent amber as VaultView's list-row highlight
    // (SearchHighlightSegment style) — kept as one literal here rather
    // than a shared resource since this is set imperatively on a Run, not
    // through XAML/DynamicResource.
    private static readonly System.Windows.Media.Brush SearchHighlightBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0xFF, 0xD5, 0x4F));

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

        EmptyStateText.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;

        Render();
    }

    private void Render()
    {
        var c = _current;
        if (c is null) return;

        RenderAvatar(c);
        SetHighlightedText(TitleText, c.Label);

        LockIcon.Visibility = c.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        if (c.IsLocked)
        {
            var sessionUnlocked = IsSessionUnlocked?.Invoke(c.Id) ?? false;
            LockIcon.Symbol = sessionUnlocked
                ? Wpf.Ui.Controls.SymbolRegular.LockOpen24
                : Wpf.Ui.Controls.SymbolRegular.LockClosed24;
        }

        HealthText.Text = $"Updated {c.UpdatedAtUtc:g}";
        UpdatedText.Text = $"Modified: {c.UpdatedAtUtc:g}";
        CreatedText.Text = $"Created: {c.CreatedAtUtc:g}";

        FavoriteButton.Icon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.Star24,
            Foreground = c.IsFavorite
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B))
                : (System.Windows.Media.Brush)FindResource("Sifra.TextSecondaryBrush"),
        };

        var tags = c.Tags ?? Array.Empty<string>();
        TagsPanel.Visibility = tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // A tag missing from the registry (e.g. added via the CLI) has no
        // known color — fall back to the same neutral gray used elsewhere.
        var colorByName = _services.Tags.List().ToDictionary(t => t.Name, t => t.Color, StringComparer.OrdinalIgnoreCase);
        TagsList.ItemsSource = tags.Select(t => new TagChipViewModel
        {
            Name = t,
            ColorBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    colorByName.TryGetValue(t, out var hex) ? hex : "#7F8C8D")),
        }).ToList();

        // "Notes" is pulled out of the generic Fields list and given its own
        // section here, mirroring the same special treatment the Add/Edit
        // window's Notes tab gives it — it's still just a field named
        // "Notes" under the hood, this is purely a display-layer split.
        var notesField = c.Fields.FirstOrDefault(f => string.Equals(f.Name, "Notes", StringComparison.OrdinalIgnoreCase));
        var hasNotes = notesField is { Value.Length: > 0 };
        NotesPanel.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;
        SetHighlightedText(NotesText, notesField?.Value ?? string.Empty);

        // An empty field is just noise — nothing to reveal, copy, or act on
        // — so it's hidden here entirely rather than shown blank, and (see
        // PasswordHealthService) excluded from weak/reused/breach analysis.
        FieldsList.ItemsSource = c.Fields.Where(f => f != notesField && f.Value.Length > 0).Select(BuildFieldRow).ToList();

        RenderAttachments();
    }

    // Builds the avatar's visual content according to the credential's Icon
    // metadata: a custom/website-fetched image, a Fluent symbol on a
    // colored circle, or the default initial-letter-on-color fallback.
    private void RenderAvatar(CredentialView c)
    {
        var icon = c.Icon;
        AvatarBorder.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                icon?.BackgroundColorHex ?? "#5B8DEF"));

        if (icon?.Kind is CredentialIconKind.WebsiteFavicon or CredentialIconKind.Custom)
        {
            var bytes = _services.CredentialIcons.GetImage(c.Id);
            if (bytes is not null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    using (var stream = new MemoryStream(bytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                    }

                    var image = new Image { Source = bitmap, Stretch = System.Windows.Media.Stretch.UniformToFill };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    AvatarBorder.Background = System.Windows.Media.Brushes.Transparent;
                    AvatarBorder.Child = image;
                    return;
                }
                catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
                {
                    // Failure path: a corrupted icon image falls through to the letter/symbol below rather than crashing.
                }
            }
        }

        if (icon?.Kind == CredentialIconKind.Symbol && icon.SymbolName is not null
            && Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(icon.SymbolName, out var symbol))
        {
            AvatarBorder.Child = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = symbol,
                FontSize = 20,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }

        AvatarBorder.Child = new TextBlock
        {
            Text = c.Label.Length > 0 ? c.Label[..1].ToUpperInvariant() : "?",
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void OnAvatarClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AvatarBorder.ContextMenu!.PlacementTarget = AvatarBorder;
        AvatarBorder.ContextMenu.IsOpen = true;
    }

    private async void OnUseWebsiteIconClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        var websiteUrl = _current.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Website)?.Value;
        if (string.IsNullOrWhiteSpace(websiteUrl))
        {
            ThemedMessageBox.Show(Window.GetWindow(this), "This credential has no Website field to fetch an icon from.", "Sifra");
            return;
        }

        var found = await _services.CredentialIcons.SetFromWebsiteAsync(_current.Id, websiteUrl, CancellationToken.None);
        if (!found)
        {
            ThemedMessageBox.Show(Window.GetWindow(this), "Could not find an icon for this website.", "Sifra", ThemedMessageBox.Icon.Warning);
            return;
        }

        ShowCredential(_current.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSelectSymbolClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        var dialog = new SelectSymbolWindow(_current.Icon?.SymbolName, _current.Icon?.BackgroundColorHex) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.SelectedSymbolName is not null)
        {
            _services.CredentialIcons.SetSymbol(_current.Id, dialog.SelectedSymbolName, dialog.SelectedColorHex);
            ShowCredential(_current.Id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSelectColorClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        var dialog = new SelectColorWindow(_current.Icon?.BackgroundColorHex) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            _services.CredentialIcons.SetColor(_current.Id, dialog.SelectedColorHex);
            ShowCredential(_current.Id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnUseCustomIconClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Use custom icon",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            _services.CredentialIcons.SetCustom(_current.Id, bytes);
            ShowCredential(_current.Id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (IOException)
        {
            // Failure path: an unreadable source file (deleted/locked between picking it and reading it) must not crash the app.
            ThemedMessageBox.Show(Window.GetWindow(this), "Could not read that file.", "Sifra", ThemedMessageBox.Icon.Warning);
        }
    }

    private void RenderAttachments()
    {
        if (_current is null) return;

        var attachments = _services.Attachments.List(_current.Id);

        var images = attachments.Where(a => a.Kind == AttachmentKind.Image).ToList();
        ImagesPanel.Visibility = images.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ImagesList.ItemsSource = images.Select(BuildImageThumbnail).ToList();

        var files = attachments.Where(a => a.Kind == AttachmentKind.File).ToList();
        FilesPanel.Visibility = files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilesList.ItemsSource = files.Select(BuildFileRow).ToList();
    }

    private FrameworkElement BuildImageThumbnail(CredentialAttachment attachment)
    {
        var container = new Border
        {
            Width = 80,
            Height = 80,
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = attachment.FileName,
        };

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

            container.Child = new System.Windows.Controls.Image { Source = bitmap, Stretch = System.Windows.Media.Stretch.UniformToFill };
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
        {
            // Failure path: a corrupted/unrecognized image must not crash the app.
            container.Child = new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        container.MouseLeftButtonUp += (_, _) => SaveAttachment(attachment);
        return container;
    }

    private FrameworkElement BuildFileRow(CredentialAttachment attachment)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new Wpf.Ui.Controls.TextBlock { Text = attachment.FileName, VerticalAlignment = VerticalAlignment.Center };
        nameText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextPrimaryBrush");
        Grid.SetColumn(nameText, 0);
        row.Children.Add(nameText);

        AddButtonColumn(row, "ArrowDownload24", "Save", () => SaveAttachment(attachment));

        return row;
    }

    // Attachments are never opened/executed in place — a stored file could be
    // of any type (script, executable, macro document), so the only supported
    // action is saving the decrypted bytes to a location the user picks.
    private void SaveAttachment(CredentialAttachment attachment)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = attachment.FileName };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
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
            // Failure path: destination locked/unwritable must not crash the app.
            ThemedMessageBox.Show(Window.GetWindow(this), "Could not save this attachment.", "Sifra", ThemedMessageBox.Icon.Warning);
        }
    }

    // Every section of the detail pane (each field, Notes, Images, Files)
    // gets its own bottom-border divider so they read as clearly separate
    // blocks, matching the reference design, rather than blurring together
    // with just vertical spacing.
    private FrameworkElement BuildFieldRow(CustomFieldView field) => WrapWithDivider(BuildFieldContent(field));

    private static Border WrapWithDivider(FrameworkElement content)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 14),
            Margin = new Thickness(0, 0, 0, 14),
            Child = content,
        };
        border.SetResourceReference(Border.BorderBrushProperty, "Sifra.BorderBrush");
        return border;
    }

    private FrameworkElement BuildFieldContent(CustomFieldView field)
    {
        var panel = new StackPanel { Tag = field };
        var caption = new Wpf.Ui.Controls.TextBlock
        {
            Text = $"{field.Name} ({CustomFieldTypeDisplay.DisplayNameFor(field.Type)})",
            FontTypography = Wpf.Ui.Controls.FontTypography.Caption,
        };
        caption.SetResourceReference(Control.ForegroundProperty, "Sifra.TextSecondaryBrush");
        panel.Children.Add(caption);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(row);

        if (field.Type == CustomFieldType.OneTimePassword)
        {
            var otpText = new Wpf.Ui.Controls.TextBlock
            {
                Name = "OtpCode",
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            otpText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextPrimaryBrush");
            Grid.SetColumn(otpText, 0);
            row.Children.Add(otpText);
            UpdateOtpText(otpText, field.Value);
            AddButtonColumn(row, "Copy24", "Copy code", () => CopyToClipboard(field.Value));
            return panel;
        }

        if (field.Type == CustomFieldType.Website && Uri.TryCreate(field.Value, UriKind.Absolute, out _))
        {
            // A plain TextBlock rather than Wpf.Ui's HyperlinkButton: that control's
            // built-in hover style pulled from the WPF-UI theme accent, which barely
            // differed from this app's navy background and was hard to see on hover.
            // Owning both states directly guarantees a visible color change.
            var link = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextDecorations = TextDecorations.Underline,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            SetHighlightedText(link, field.Value);
            link.SetResourceReference(TextBlock.ForegroundProperty, "Sifra.LinkBrush");
            link.MouseEnter += (_, _) => link.SetResourceReference(TextBlock.ForegroundProperty, "Sifra.LinkHoverBrush");
            link.MouseLeave += (_, _) => link.SetResourceReference(TextBlock.ForegroundProperty, "Sifra.LinkBrush");
            link.MouseLeftButtonUp += (_, _) => OpenUrl(field.Value);
            Grid.SetColumn(link, 0);
            row.Children.Add(link);
            AddButtonColumn(row, "Copy24", "Copy website", () => CopyToClipboard(field.Value));
            return panel;
        }

        var isSensitive = field.Type is CustomFieldType.Password or CustomFieldType.Pin or CustomFieldType.Secret;
        var valueText = new Wpf.Ui.Controls.TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        valueText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextPrimaryBrush");
        Grid.SetColumn(valueText, 0);
        row.Children.Add(valueText);

        if (isSensitive)
        {
            // Masked by default, so nothing to highlight until revealed —
            // highlighting bullet characters would be meaningless.
            valueText.Inlines.Add(new Run(new string('•', 8)));
            var revealed = false;
            AddButtonColumn(row, "Eye24", "Show/hide", () =>
            {
                revealed = !revealed;
                if (revealed)
                {
                    SetHighlightedText(valueText, field.Value);
                }
                else
                {
                    valueText.Inlines.Clear();
                    valueText.Inlines.Add(new Run(new string('•', 8)));
                }
            });
        }
        else
        {
            SetHighlightedText(valueText, field.Value);
        }

        AddButtonColumn(row, "Copy24", "Copy value", () => CopyToClipboard(field.Value));

        if (field.Type == CustomFieldType.Password)
        {
            AddButtonColumn(row, "History24", "View password history", () => ShowPasswordHistory(field.Name));

            if (!string.IsNullOrEmpty(field.Value))
            {
                panel.Children.Add(BuildStrengthMeter(field.Value));
            }
        }

        return panel;
    }

    // A local, offline crack-time estimate (see PasswordStrengthEstimator) —
    // never sent anywhere, computed from the plaintext already decrypted
    // for display. Purely a strength hint, not a security verdict.
    private static FrameworkElement BuildStrengthMeter(string password)
    {
        var estimate = PasswordStrengthEstimator.Estimate(password);
        var levelBrush = PasswordStrengthUi.ColorFor(estimate.Level);

        // Fixed, compact width rather than stretching across the whole
        // field — this is a small strength hint, not a progress bar for
        // the row itself.
        var container = new StackPanel { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Width = 160 };

        var segments = new Grid();
        for (var i = 0; i < 4; i++)
        {
            segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var segment = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
            };
            segment.SetResourceReference(Border.BackgroundProperty, "Sifra.BorderBrush");
            if (i < estimate.FilledSegments)
            {
                segment.Background = levelBrush;
            }

            Grid.SetColumn(segment, i);
            segments.Children.Add(segment);
        }

        container.Children.Add(segments);

        var crackTimeText = new Wpf.Ui.Controls.TextBlock
        {
            Text = $"Crack time: {estimate.CrackTimeDisplay}",
            FontTypography = Wpf.Ui.Controls.FontTypography.Caption,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
        };
        crackTimeText.SetResourceReference(Control.ForegroundProperty, "Sifra.TextSecondaryBrush");
        container.Children.Add(crackTimeText);

        return container;
    }

    private void ShowPasswordHistory(string fieldName)
    {
        if (_current is null) return;

        var window = new PasswordHistoryWindow(_services, _vaultCredential, _current.Id, fieldName)
        {
            Owner = Window.GetWindow(this),
        };
        window.ShowDialog();
    }

    private static void AddButtonColumn(Grid row, string symbolName, string toolTip, Action onClick)
    {
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var symbol = Enum.Parse<Wpf.Ui.Controls.SymbolRegular>(symbolName);
        var button = new Wpf.Ui.Controls.Button { Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = symbol }, ToolTip = toolTip };
        button.Click += (_, _) => onClick();
        Grid.SetColumn(button, row.ColumnDefinitions.Count - 1);
        row.Children.Add(button);
    }

    private void CopyToClipboard(string value)
    {
        try
        {
            Clipboard.SetText(value);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Failure path: another process briefly holding the clipboard must not crash the app.
            ThemedMessageBox.Show(Window.GetWindow(this), "Could not copy to the clipboard.", "Sifra", ThemedMessageBox.Icon.Warning);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Failure path: an unreachable/invalid URL must not crash the app.
            ThemedMessageBox.Show(Window.GetWindow(this), "Could not open this URL.", "Sifra", ThemedMessageBox.Icon.Warning);
        }
    }

    private void RefreshOtpFields()
    {
        if (_current is null) return;

        foreach (var child in FieldsList.Items)
        {
            if (child is StackPanel { Tag: CustomFieldView field } panel && field.Type == CustomFieldType.OneTimePassword)
            {
                var row = panel.Children.OfType<Grid>().FirstOrDefault();
                var otpText = row?.Children.OfType<Wpf.Ui.Controls.TextBlock>().FirstOrDefault();
                if (otpText is not null)
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

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || !IsUnlockedForModification()) return;
        _services.Credentials.SetFavorite(_current.Id, !_current.IsFavorite);
        ShowCredential(_current.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsUnlockedForModification() =>
        _current is null || (EnsureUnlockedForModification?.Invoke(_current.Id, _current.Label, _current.IsLocked) ?? true);

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || !IsUnlockedForModification()) return;

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

    private void OnSetTagsClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || !IsUnlockedForModification()) return;

        var window = new SetTagsWindow(_services.Tags, _current.Tags ?? Array.Empty<string>())
        {
            Owner = Window.GetWindow(this),
        };

        if (window.ShowDialog() == true)
        {
            _services.Credentials.SetTags(_current.Id, window.SelectedTags);
            ShowCredential(_current.Id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
