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
        _detailView.Changed += (_, _) => Refresh();
        DetailHost.Child = _detailView;
        _detailView.ShowEmpty();

        // Set after InitializeComponent (not via XAML IsChecked="True") —
        // XAML's IsChecked raises Checked synchronously during parsing,
        // before later-declared elements like CredentialsList exist yet,
        // which crashed ApplyFilter() with a NullReferenceException.
        AllItemsCategory.IsChecked = true;
        Refresh();
    }

    private void Refresh()
    {
        _allRows = _services.Credentials.List(_vaultCredential)
            .Select(v => new CredentialRow(v))
            .ToList();

        BuildLabelCategories();
        UpdateCategoryCounts();
        ApplyFilter();
        _ = AnalyzeHealthAsync(_allRows);
    }

    private void BuildLabelCategories()
    {
        var previousSelection = _category;
        LabelCategoryPanel.Children.Clear();

        var distinctLabels = _allRows.SelectMany(r => r.Labels).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(l => l).ToList();
        foreach (var label in distinctLabels)
        {
            var count = _allRows.Count(r => r.Labels.Contains(label, StringComparer.OrdinalIgnoreCase));
            var radio = new RadioButton
            {
                Style = (Style)FindResource("CategoryItem"),
                Tag = label,
                Content = new TextBlock { Text = $"{label}  {count}" },
                IsChecked = previousSelection == label,
            };
            radio.Checked += OnCategoryChanged;
            LabelCategoryPanel.Children.Add(radio);
        }

        // The label that was selected may no longer exist (e.g. its last credential was deleted) — fall back to All Items.
        if (previousSelection != "All" && previousSelection != "Favorites" && previousSelection != "Weak"
            && previousSelection != "Reused" && previousSelection != "Compromised"
            && !distinctLabels.Contains(previousSelection, StringComparer.OrdinalIgnoreCase))
        {
            _category = "All";
            AllItemsCategory.IsChecked = true;
        }
    }

    private void UpdateCategoryCounts()
    {
        AllItemsCategoryText.Text = $"All Items  {_allRows.Count}";
        FavoritesCategoryText.Text = $"Favorites  {_allRows.Count(r => r.IsFavorite)}";
        WeakCategoryText.Text = $"Weak Passwords  {_allRows.Count(r => r.IsWeak)}";
        ReusedCategoryText.Text = $"Reused Passwords  {_allRows.Count(r => r.IsReused)}";
        CompromisedCategoryText.Text = $"Compromised  {_allRows.Count(r => r.IsBreached)}";
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
            _ => _allRows.Where(r => r.Labels.Contains(_category, StringComparer.OrdinalIgnoreCase)),
        };

        var query = SearchBox.Text?.Trim() ?? string.Empty;
        if (query.Length > 0)
        {
            rows = rows.Where(r =>
                r.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (r.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        CredentialsList.ItemsSource = rows.ToList();
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

        if (window.ShowDialog() == true)
        {
            Refresh();
        }
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

        if (window.ShowDialog() == true)
        {
            Refresh();
        }
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
