using System.Windows;
using System.Windows.Controls;
using Sifra.Vault.Health;

namespace Sifra.Desktop;

public partial class VaultView : UserControl
{
    private readonly AppServices _services;
    private readonly string _vaultCredential;
    private readonly CredentialDetailView _detailView;
    private List<CredentialRow> _allRows = new();
    private string _category = "All"; // "All" | "Favorites" | "Weak" | "Reused" | "Compromised" | a label name

    public event EventHandler? LockRequested;

    public VaultView(AppServices services, string vaultCredential)
    {
        InitializeComponent();
        _services = services;
        _vaultCredential = vaultCredential;

        _detailView = new CredentialDetailView(services, vaultCredential);
        // Same reselect-after-rebuild fix as OnEditCredentialClick — this
        // fires from the detail pane's own Edit button and its Favorite
        // toggle, both of which would otherwise drop the selection too.
        _detailView.Changed += (_, _) => Refresh((CredentialsList.SelectedItem as CredentialRow)?.Id);
        DetailHost.Child = _detailView;
        _detailView.ShowEmpty();

        // A tag can be created from the modal "Set Tags" dialog while
        // this screen sits behind it — refresh the sidebar the moment that
        // happens rather than waiting for the Add/Edit Credential window to
        // close.
        _services.Tags.TagAdded += (_, _) => BuildTagCategories();

        // Set after InitializeComponent (not via XAML IsChecked="True") —
        // XAML's IsChecked raises Checked synchronously during parsing,
        // before later-declared elements like CredentialsList exist yet,
        // which crashed ApplyFilter() with a NullReferenceException.
        AllItemsCategory.IsChecked = true;
        Refresh();
    }

    private void Refresh(string? reselectId = null)
    {
        var tagColors = _services.Tags.List().ToDictionary(t => t.Name, t => t.Color, StringComparer.OrdinalIgnoreCase);
        _allRows = _services.Credentials.List(_vaultCredential)
            .Select(v => new CredentialRow(v, tagColors, _services.CredentialIcons))
            .ToList();

        BuildTagCategories();
        UpdateCategoryCounts();
        ApplyFilter();

        // Rebuilding the list creates all-new CredentialRow instances, which
        // would otherwise silently drop the selection on every edit — a real
        // pain with a long vault, since finding the same item again means
        // re-scrolling/re-searching for it.
        if (reselectId is not null && CredentialsList.ItemsSource is IEnumerable<CredentialRow> rows)
        {
            CredentialsList.SelectedItem = rows.FirstOrDefault(r => r.Id == reselectId);
        }

        _ = AnalyzeHealthAsync(_allRows);
    }

    private void BuildTagCategories()
    {
        var previousSelection = _category;
        TagCategoryPanel.Children.Clear();

        // The sidebar shows every registered tag, not just ones already in
        // use — a newly created tag (0 credentials so far) should still
        // appear immediately. Registered tags keep the registry's
        // pinned-then-alphabetical order; any tag name found on a
        // credential but missing from the registry (e.g. added via the CLI)
        // is appended alphabetically so it doesn't silently disappear.
        var registeredTags = _services.Tags.List();
        var registeredNames = registeredTags.Select(t => t.Name).ToList();
        var colorByName = registeredTags.ToDictionary(t => t.Name, t => t.Color, StringComparer.OrdinalIgnoreCase);
        var usedNames = _allRows.SelectMany(r => r.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var unregisteredUsedNames = usedNames
            .Where(n => !registeredNames.Contains(n, StringComparer.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        var distinctTags = registeredNames.Concat(unregisteredUsedNames).ToList();
        foreach (var tag in distinctTags)
        {
            var count = _allRows.Count(r => r.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
            // A tag used on a credential but missing from the registry (e.g.
            // added via the CLI, which doesn't create a registry entry) has
            // no known color — fall back to the default neutral swatch.
            var colorHex = colorByName.TryGetValue(tag, out var c) ? c : "#7F8C8D";
            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Tag24,
                Filled = true,
                Foreground = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(colorHex)!,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            // Icon and label share the flexible column; the count gets its
            // own Auto column so it lands flush against the row's right
            // edge instead of trailing right after the label text.
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(icon, 0);
            var nameText = new TextBlock
            {
                Text = tag,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 6, 0),
            };
            Grid.SetColumn(nameText, 1);
            var countText = new TextBlock { Text = count.ToString(), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(countText, 2);
            content.Children.Add(icon);
            content.Children.Add(nameText);
            content.Children.Add(countText);

            var radio = new RadioButton
            {
                Style = (Style)FindResource("CategoryItem"),
                Tag = tag,
                Content = content,
                IsChecked = previousSelection == tag,
            };
            radio.Checked += OnCategoryChanged;
            TagCategoryPanel.Children.Add(radio);
        }

        // The tag that was selected may no longer exist (e.g. its last credential was deleted) — fall back to All Items.
        if (previousSelection != "All" && previousSelection != "Favorites" && previousSelection != "Weak"
            && previousSelection != "Reused" && previousSelection != "Compromised"
            && !distinctTags.Contains(previousSelection, StringComparer.OrdinalIgnoreCase))
        {
            _category = "All";
            AllItemsCategory.IsChecked = true;
        }
    }

    private void UpdateCategoryCounts()
    {
        AllItemsCategoryText.Text = _allRows.Count.ToString();
        FavoritesCategoryText.Text = _allRows.Count(r => r.IsFavorite).ToString();
        WeakCategoryText.Text = _allRows.Count(r => r.IsWeak).ToString();
        ReusedCategoryText.Text = _allRows.Count(r => r.IsReused).ToString();
        CompromisedCategoryText.Text = _allRows.Count(r => r.IsBreached).ToString();
    }

    private void OnCategoryChanged(object sender, RoutedEventArgs e)
    {
        _category = (string)((RadioButton)sender).Tag;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<CredentialRow> rows = _category switch
        {
            "All" => _allRows,
            "Favorites" => _allRows.Where(r => r.IsFavorite),
            "Weak" => _allRows.Where(r => r.IsWeak),
            "Reused" => _allRows.Where(r => r.IsReused),
            "Compromised" => _allRows.Where(r => r.IsBreached),
            _ => _allRows.Where(r => r.Tags.Contains(_category, StringComparer.OrdinalIgnoreCase)),
        };

        var query = SearchBox.Text?.Trim() ?? string.Empty;
        if (query.Length > 0)
        {
            rows = rows.Where(r =>
                r.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                r.FieldValues.Any(v => v.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        // Sorted alphabetically by title for easy scanning in a long vault —
        // storage order (creation order) is untouched, this only affects
        // what's displayed.
        CredentialsList.ItemsSource = rows.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task AnalyzeHealthAsync(List<CredentialRow> rows)
    {
        IReadOnlyList<CredentialHealthReport> reports;
        try
        {
            reports = await _services.PasswordHealth.AnalyzeAllAsync(_vaultCredential, _services.BreachChecker, CancellationToken.None);
        }
        catch
        {
            // Failure path: analysis failing must not crash the app or
            // leave rows silently stuck — show it plainly instead.
            foreach (var row in rows)
            {
                row.HealthStatus = "Analysis failed";
                row.HealthLevel = HealthLevel.Unavailable;
            }
            return;
        }

        var byId = reports.ToDictionary(r => r.CredentialId);
        foreach (var row in rows)
        {
            if (!byId.TryGetValue(row.Id, out var report))
            {
                row.HealthStatus = "Unknown";
                row.HealthLevel = HealthLevel.Unavailable;
                continue;
            }

            row.IsWeak = report.IsWeak;
            row.IsReused = report.IsReused;
            row.IsBreached = report.BreachOutcome == BreachCheckOutcome.Breached;
            (row.HealthStatus, row.HealthLevel) = Describe(report);
        }

        UpdateCategoryCounts();
        ApplyFilter();
    }

    private static (string Text, HealthLevel Level) Describe(CredentialHealthReport report)
    {
        if (report.BreachOutcome == BreachCheckOutcome.Breached)
        {
            return ($"Breached ({report.BreachCount:N0}x)", HealthLevel.Breached);
        }

        if (report.BreachOutcome == BreachCheckOutcome.CheckUnavailable)
        {
            return ("Unavailable", HealthLevel.Unavailable);
        }

        if (report.IsWeak)
        {
            return ("Weak", HealthLevel.Weak);
        }

        return ("OK", HealthLevel.Ok);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = CredentialsList.SelectedItem as CredentialRow;
        EditCredentialButton.IsEnabled = selected is not null;
        DeleteCredentialButton.IsEnabled = selected is not null;

        if (selected is null)
        {
            _detailView.ShowEmpty();
        }
        else
        {
            _detailView.ShowCredential(selected.Id);
        }
    }

    private void OnCredentialsListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CredentialsList.SelectedItem is CredentialRow)
        {
            OnEditCredentialClick(sender, e);
        }
    }

    private void OnAddCredentialClick(object sender, RoutedEventArgs e)
    {
        var window = new AddCredentialWindow(_services, _vaultCredential)
        {
            Owner = Window.GetWindow(this),
        };

        // Refresh regardless of dialog result — the user may have created a
        // new label via "Set labels" and then cancelled the credential
        // itself; the label registry change should still show up.
        window.ShowDialog();
        Refresh();
    }

    private void OnEditCredentialClick(object sender, RoutedEventArgs e)
    {
        if (CredentialsList.SelectedItem is not CredentialRow selected)
        {
            return;
        }

        var full = _services.Credentials.GetById(_vaultCredential, selected.Id);
        var window = new AddCredentialWindow(_services, _vaultCredential, full)
        {
            Owner = Window.GetWindow(this),
        };

        // Refresh regardless of dialog result — see OnAddCredentialClick.
        // Re-select the edited item afterward: rebuilding the list otherwise
        // drops the selection, which is a real pain to recover in a long
        // vault (re-scroll or re-search to find the same item again).
        window.ShowDialog();
        Refresh(selected.Id);
    }

    private void OnDeleteCredentialClick(object sender, RoutedEventArgs e)
    {
        if (CredentialsList.SelectedItem is not CredentialRow selected)
        {
            return;
        }

        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"Delete \"{selected.Label}\"? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _services.Credentials.Delete(selected.Id);
            _services.Attachments.DeleteAllForCredential(selected.Id); // no orphaned encrypted blobs left on disk
            _services.PasswordHistory.DeleteAllForCredential(selected.Id); // no orphaned history entries left on disk
            _services.CredentialIcons.DeleteAllForCredential(selected.Id); // no orphaned icon image left on disk
            _detailView.ShowEmpty();
            Refresh();
        }
    }

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        LockRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaceholderToolClick(object sender, RoutedEventArgs e)
    {
        var name = (string)((FrameworkElement)sender).Tag;
        MessageBox.Show(
            Window.GetWindow(this),
            $"{name} is not built yet and will be implemented according to the build plan.",
            "Sifra",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
