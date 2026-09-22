using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sifra.Vault.Auth;
using Sifra.Vault.Credentials;
using Sifra.Vault.Dropbox;
using Sifra.Vault.GoogleDrive;
using Sifra.Vault.PCloud;
using Sifra.Vault.Health;
using Sifra.Vault.Sync;

namespace Sifra.Desktop;

public partial class VaultView : UserControl
{
    private readonly AppServices _services;
    private readonly string _vaultCredential;
    private readonly CredentialDetailView _detailView;
    private readonly SyncScheduler _syncScheduler;
    private List<CredentialRow> _allRows = new();
    private string _category = "All"; // "All" | "Favorites" | "Weak" | "Reused" | "Compromised" | a label name

    public event EventHandler? LockRequested;
    public event EventHandler? SettingsChanged;

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
        // The detail pane's own Edit/Favorite/Set-tags buttons are a second
        // path (besides this view's toolbar and context menu) that can
        // modify a credential — they need the exact same Lock guard.
        _detailView.EnsureUnlockedForModification = EnsureUnlockedForModification;
        _detailView.IsSessionUnlocked = id => _sessionUnlockedIds.Contains(id);
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
        TryRefresh();
        UpdateSyncPanel();

        // Background sync: an immediate attempt on unlock, then every 5
        // minutes while this screen stays up. See SyncScheduler's remarks
        // on why it never opens a browser on its own.
        _syncScheduler = new SyncScheduler(_services);
        _syncScheduler.StatusChanged += (_, status) =>
        {
            UpdateSyncPanel(status);
            if (!TryRefresh())
            {
                UpdateSyncPanel("Synced, but the list couldn't refresh — a record could not be decrypted.");
            }
        };
        _syncScheduler.Start();
    }

    /// <summary>
    /// Keeps the reserved sync-status area at the bottom of the category
    /// pane current — provider name/icon always shown, plus whatever the
    /// last known status line is (idle, syncing, last-synced time, or a
    /// decrypt-failure note). A null status means "just show the provider,
    /// don't touch the status line" — used on startup before any sync has
    /// happened yet this session.
    /// </summary>
    private void UpdateSyncPanel(string? status = null)
    {
        SyncPanelProviderText.Text = _services.ActiveSyncProvider is { } provider
            ? provider.ToString()
            : "No cloud sync";

        if (status is not null)
        {
            SyncPanelStatusText.Text = status;
        }
        else if (_services.ActiveSyncProvider is null)
        {
            SyncPanelStatusText.Text = "Set up sync in Options to sync across devices.";
        }
        else
        {
            SyncPanelStatusText.Text = "Not synced yet this session.";
        }
    }

    /// <summary>
    /// Refresh() (and BuildTagCategories()) can throw if any stored
    /// credential can't be decrypted with this vault's key — e.g.
    /// ciphertext from an unrelated vault that ended up merged in via a
    /// misconfigured cloud file/folder. CredentialService.List()
    /// deliberately throws on the first bad record rather than returning
    /// garbage (see CredentialServiceTests), which is correct, but nothing
    /// that merely refreshes the view — an app launch, an unlock, a
    /// background sync tick — should ever crash the whole app over it.
    /// </summary>
    /// <returns>False if the refresh failed; the credential list may be stale until this is resolved.</returns>
    private bool TryRefresh(string? reselectId = null)
    {
        try
        {
            Refresh(reselectId);
            BuildTagCategories();
            return true;
        }
        catch (Sifra.Vault.Crypto.VaultDecryptionFailedException)
        {
            return false;
        }
    }

    /// <summary>Called by ShellView (via MainWindow.ShowUnlock) right before this screen is torn down, so the background timer doesn't keep ticking after the vault locks.</summary>
    public void StopBackgroundSync() => _syncScheduler.Stop();

    private void Refresh(string? reselectId = null)
    {
        var tagColors = _services.Tags.List().ToDictionary(t => t.Name, t => t.Color, StringComparer.OrdinalIgnoreCase);
        _allRows = _services.Credentials.List(_vaultCredential)
            .Select(v => new CredentialRow(v, tagColors, _services.CredentialIcons, _sessionUnlockedIds.Contains(v.Id)))
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
            var count = _allRows.Count(r => r.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) && !r.IsArchived && !r.IsDeleted);
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
        if (previousSelection != "All" && previousSelection != "Favorites" && previousSelection != "Locked" && previousSelection != "Weak"
            && previousSelection != "Reused" && previousSelection != "Compromised"
            && previousSelection != "Archived" && previousSelection != "Trash"
            && !distinctTags.Contains(previousSelection, StringComparer.OrdinalIgnoreCase))
        {
            _category = "All";
            AllItemsCategory.IsChecked = true;
        }
    }

    private void UpdateCategoryCounts()
    {
        var active = _allRows.Where(r => !r.IsArchived && !r.IsDeleted).ToList();
        AllItemsCategoryText.Text = active.Count.ToString();
        FavoritesCategoryText.Text = active.Count(r => r.IsFavorite).ToString();
        LockedCategoryText.Text = active.Count(r => r.IsLocked).ToString();
        WeakCategoryText.Text = active.Count(r => r.IsWeak).ToString();
        ReusedCategoryText.Text = active.Count(r => r.IsReused).ToString();
        CompromisedCategoryText.Text = active.Count(r => r.IsBreached).ToString();
        ArchivedCategoryText.Text = _allRows.Count(r => r.IsArchived && !r.IsDeleted).ToString();
        TrashCategoryText.Text = _allRows.Count(r => r.IsDeleted).ToString();
    }

    private const int StaleTrashDays = 30;

    /// <summary>
    /// A nudge, never an automatic purge — per product decision, Trash
    /// never empties itself. Shown only while viewing Trash, and only when
    /// at least one item has actually sat there 30+ days.
    /// </summary>
    private void UpdateStaleTrashNotice()
    {
        if (_category != "Trash")
        {
            StaleTrashNoticeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-StaleTrashDays);
        var staleCount = _allRows.Count(r => r.IsDeleted && r.DeletedAtUtc is not null && r.DeletedAtUtc <= cutoff);

        if (staleCount == 0)
        {
            StaleTrashNoticeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        StaleTrashNoticeText.Text = $"{staleCount} item(s) have been in Trash for {StaleTrashDays}+ days.";
        StaleTrashNoticeBorder.Visibility = Visibility.Visible;
    }

    private CredentialRow? _contextMenuRow;

    /// <summary>
    /// Ids unlocked for the rest of THIS session only — cleared on every
    /// fresh unlock of the vault (a new VaultView instance), never
    /// persisted. Being in this set does not change IsLocked; it only
    /// authorizes this running app instance to bypass the lock guard for
    /// that credential until the vault re-locks. See Credential's own
    /// remarks on why Lock is a UI-layer concern, not the service's.
    /// </summary>
    private readonly HashSet<string> _sessionUnlockedIds = new();

    /// <summary>
    /// Right-click doesn't move ListBox.SelectedItem on its own, so this
    /// finds whichever row the cursor landed on, selects it (matching the
    /// usual click-then-right-click UX), and toggles which menu items make
    /// sense for its current state:
    ///  - Trashed: Restore + Delete permanently only.
    ///  - Locked, not yet unlocked this session: Unlock... only — nothing
    ///    that could modify or delete it is reachable until then.
    ///  - Locked and already unlocked this session: the full normal menu,
    ///    plus Remove Lock... instead of Lock.
    ///  - Otherwise: the full normal menu, plus Lock.
    /// </summary>
    private void OnCredentialContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject)?.FindAncestorDataContext<CredentialRow>();
        if (row is null)
        {
            e.Handled = true; // right-clicked empty space below the last row — no menu
            return;
        }

        CredentialsList.SelectedItem = row;
        _contextMenuRow = row;

        var isTrashed = row.IsDeleted;
        var isLockedAndNotUnlocked = row.IsLocked && !_sessionUnlockedIds.Contains(row.Id);

        // Locked-and-not-unlocked overrides everything else — nothing but
        // Unlock is reachable, trashed or not (a locked item can't
        // actually reach Trash via normal UI anyway, since Delete itself
        // is one of the blocked actions).
        if (isLockedAndNotUnlocked)
        {
            ContextEditItem.Visibility = Visibility.Collapsed;
            ContextSetTagsItem.Visibility = Visibility.Collapsed;
            ContextFavoriteItem.Visibility = Visibility.Collapsed;
            // Duplicate, Copy as Text, and Export are all read-only with
            // respect to the original item — Duplicate creates a brand-new
            // credential rather than modifying this one, so Lock (which
            // guards against modifying/deleting the original) has no
            // reason to block any of the three just because the item is
            // locked-and-not-session-unlocked.
            ContextDuplicateItem.Visibility = Visibility.Visible;
            ContextCopyAsTextItem.Visibility = Visibility.Visible;
            ContextExportItem.Visibility = Visibility.Visible;
            ContextSeparator1.Visibility = Visibility.Collapsed;
            ContextArchiveItem.Visibility = Visibility.Collapsed;
            ContextLockItem.Visibility = Visibility.Collapsed;
            ContextRemoveLockItem.Visibility = Visibility.Collapsed;
            ContextDeleteItem.Visibility = Visibility.Collapsed;
            ContextRestoreItem.Visibility = Visibility.Collapsed;
            ContextDeletePermanentlyItem.Visibility = Visibility.Collapsed;
            ContextUnlockItem.Visibility = Visibility.Visible;
            return;
        }

        ContextUnlockItem.Visibility = Visibility.Collapsed;
        ContextEditItem.Visibility = isTrashed ? Visibility.Collapsed : Visibility.Visible;
        ContextSetTagsItem.Visibility = isTrashed ? Visibility.Collapsed : Visibility.Visible;
        ContextFavoriteItem.Visibility = isTrashed ? Visibility.Collapsed : Visibility.Visible;
        ContextFavoriteItem.Header = row.IsFavorite ? "Remove from Favorites" : "Add to Favorites";
        ContextDuplicateItem.Visibility = isTrashed ? Visibility.Collapsed : Visibility.Visible;
        // Read-only actions — available regardless of trashed/locked state.
        ContextCopyAsTextItem.Visibility = Visibility.Visible;
        ContextExportItem.Visibility = Visibility.Visible;
        ContextSeparator1.Visibility = isTrashed ? Visibility.Collapsed : Visibility.Visible;
        ContextArchiveItem.Visibility = isTrashed ? Visibility.Collapsed : Visibility.Visible;
        ContextArchiveItem.Header = row.IsArchived ? "Unarchive" : "Archive";
        ContextLockItem.Visibility = !isTrashed && !row.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        ContextRemoveLockItem.Visibility = !isTrashed && row.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        // Delete stays blocked by IsLocked itself, not by session-unlock —
        // a session-unlock only permits editing; deleting a locked item
        // requires the deliberate, permanent step of Remove Lock first
        // (there's nothing left to protect once it's deleted, so this
        // doesn't conflict with Remove Lock's own "permanent" meaning).
        ContextDeleteItem.Visibility = !isTrashed && !row.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        ContextRestoreItem.Visibility = isTrashed ? Visibility.Visible : Visibility.Collapsed;
        ContextDeletePermanentlyItem.Visibility = isTrashed ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Shows the master-password gate; returns true only if it was entered correctly and the dialog was confirmed.</summary>
    private bool ConfirmMasterPassword(string message)
    {
        var window = new MasterPasswordConfirmWindow(_services.VaultAuth, message) { Owner = Window.GetWindow(this) };
        return window.ShowDialog() == true;
    }

    private void OnContextLockClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        // Locking itself needs no re-authentication — only unlocking/
        // removing a lock does. Matches the usual "locking is free,
        // unlocking costs a check" convention.
        _services.Credentials.SetLocked(row.Id, true);
        TryRefresh(row.Id);
    }

    private void OnContextUnlockClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        if (ConfirmMasterPassword($"Enter your master password to unlock \"{row.Label}\" for this session."))
        {
            _sessionUnlockedIds.Add(row.Id);
            TryRefresh(row.Id);
        }
    }

    private void OnContextRemoveLockClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        if (ConfirmMasterPassword($"Enter your master password to permanently remove the lock on \"{row.Label}\"."))
        {
            _services.Credentials.SetLocked(row.Id, false);
            _sessionUnlockedIds.Remove(row.Id); // no longer meaningful once the lock itself is gone
            TryRefresh(row.Id);
        }
    }

    private void OnContextEditClick(object sender, RoutedEventArgs e) => OnEditCredentialClick(sender, e);

    private void OnContextSetTagsClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        var window = new SetTagsWindow(_services.Tags, row.Tags) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true)
        {
            _services.Credentials.SetTags(row.Id, window.SelectedTags);
            TryRefresh(row.Id);
        }
    }

    private void OnContextToggleFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        _services.Credentials.SetFavorite(row.Id, !row.IsFavorite);
        TryRefresh(row.Id);
    }

    private void OnContextDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        var newId = _services.Credentials.Duplicate(row.Id);
        TryRefresh(newId);
    }

    private void OnContextCopyAsTextClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        try
        {
            _services.Credentials.CopyAsText(_vaultCredential, row.Id);
        }
        catch (ClipboardUnavailableException)
        {
            // Failure path: another process briefly holding the clipboard must not crash the app.
            ThemedMessageBox.Show(Window.GetWindow(this), "Could not copy to the clipboard.", "Sifra", ThemedMessageBox.Icon.Warning);
        }
    }

    private void OnContextExportClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = row.Label,
            DefaultExt = ".txt",
            Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _services.Credentials.ExportAsText(_vaultCredential, row.Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failure path: e.g. the target file is open in another program, or the folder is read-only.
            ThemedMessageBox.Show(Window.GetWindow(this), "Could not save this file.", "Sifra", ThemedMessageBox.Icon.Warning);
        }
    }

    private void OnContextToggleArchiveClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        _services.Credentials.SetArchived(row.Id, !row.IsArchived);
        // Archiving/unarchiving moves the item out of (or into) the
        // currently-viewed category — nothing to reselect afterward since
        // it may no longer be in this list at all.
        _detailView.ShowEmpty();
        TryRefresh();
    }

    private void OnContextDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is { } row)
        {
            SoftDeleteWithConfirmation(row);
        }
    }

    private void OnContextRestoreClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        _services.Credentials.Restore(row.Id);
        _detailView.ShowEmpty();
        TryRefresh();
    }

    private void OnContextDeletePermanentlyClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is not { } row)
        {
            return;
        }

        var confirmed = ThemedMessageBox.ShowConfirm(
            Window.GetWindow(this),
            $"Permanently delete \"{row.Label}\"? This cannot be undone.",
            "Delete Permanently");

        if (confirmed)
        {
            PermanentlyDelete(row);
            _detailView.ShowEmpty();
            TryRefresh();
        }
    }

    private void OnCategoryChanged(object sender, RoutedEventArgs e)
    {
        _category = (string)((RadioButton)sender).Tag;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        // Archived/Deleted items are hidden from every normal view (All,
        // Favorites, Weak/Reused/Compromised, tags) — same idea as email
        // archive/trash. Trash shows ONLY deleted items regardless of
        // archived state; Archived shows archived-but-not-deleted ones.
        IEnumerable<CredentialRow> rows = _category switch
        {
            "Trash" => _allRows.Where(r => r.IsDeleted),
            "Archived" => _allRows.Where(r => r.IsArchived && !r.IsDeleted),
            "All" => _allRows.Where(r => !r.IsArchived && !r.IsDeleted),
            "Favorites" => _allRows.Where(r => r.IsFavorite && !r.IsArchived && !r.IsDeleted),
            "Locked" => _allRows.Where(r => r.IsLocked && !r.IsArchived && !r.IsDeleted),
            "Weak" => _allRows.Where(r => r.IsWeak && !r.IsArchived && !r.IsDeleted),
            "Reused" => _allRows.Where(r => r.IsReused && !r.IsArchived && !r.IsDeleted),
            "Compromised" => _allRows.Where(r => r.IsBreached && !r.IsArchived && !r.IsDeleted),
            _ => _allRows.Where(r => r.Tags.Contains(_category, StringComparer.OrdinalIgnoreCase) && !r.IsArchived && !r.IsDeleted),
        };

        UpdateStaleTrashNotice();

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
        var sortedRows = rows.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();

        // Drives LabelSegments/SubtitleSegments (see CredentialRow) so the
        // matched substring renders highlighted in the list, not just
        // filtered — set on every row still in _allRows (not just the
        // visible ones) so a row hidden by category-but-not-search still
        // clears its stale highlight if the query changes later.
        foreach (var row in _allRows)
        {
            row.SearchQuery = query;
        }

        CredentialsList.ItemsSource = sortedRows;
        _detailView.SearchQuery = query;

        // Jump to the first match while actively searching — the standard
        // search-and-jump convention (browser Ctrl+F, address bar
        // autocomplete). Left alone when the query is cleared, so clearing
        // search doesn't yank the selection away from whatever the user
        // was already looking at.
        if (query.Length > 0 && sortedRows.Count > 0)
        {
            CredentialsList.SelectedItem = sortedRows[0];
        }
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

    /// <summary>
    /// The runtime guard for paths that don't already hide themselves for
    /// a locked item the way the context menu does — the toolbar's
    /// Edit/Delete buttons and double-click-to-edit stay enabled/reachable
    /// regardless of which row is selected. True means "proceed" (not
    /// locked, or already unlocked this session, or just successfully
    /// unlocked right now).
    /// </summary>
    private bool EnsureUnlockedForModification(CredentialRow row) =>
        EnsureUnlockedForModification(row.Id, row.Label, row.IsLocked);

    /// <summary>
    /// Also wired into CredentialDetailView.EnsureUnlockedForModification
    /// (see constructor) — the detail pane's own Edit/Favorite/Set-tags
    /// buttons are a second path that can modify a credential without
    /// going through this view's toolbar or context menu, and need the
    /// exact same guard.
    /// </summary>
    private bool EnsureUnlockedForModification(string id, string label, bool isLocked)
    {
        if (!isLocked || _sessionUnlockedIds.Contains(id))
        {
            return true;
        }

        if (ConfirmMasterPassword($"\"{label}\" is locked. Enter your master password to unlock it for this session."))
        {
            _sessionUnlockedIds.Add(id);
            return true;
        }

        return false;
    }

    private void OnEditCredentialClick(object sender, RoutedEventArgs e)
    {
        if (CredentialsList.SelectedItem is not CredentialRow selected || !EnsureUnlockedForModification(selected))
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
        if (CredentialsList.SelectedItem is CredentialRow selected)
        {
            SoftDeleteWithConfirmation(selected);
        }
    }

    /// <summary>Moves a credential to Trash, after an explicit confirmation — the same action from the toolbar button and the row's context menu.</summary>
    private void SoftDeleteWithConfirmation(CredentialRow row)
    {
        // Blocked by IsLocked itself, not session-unlock — see
        // OnCredentialContextMenuOpening's remarks on why Delete needs the
        // deliberate, permanent Remove Lock step first.
        if (row.IsLocked)
        {
            ThemedMessageBox.Show(
                Window.GetWindow(this),
                $"\"{row.Label}\" is locked. Remove the lock (right-click → Remove Lock...) before deleting it.",
                "Sifra");
            return;
        }

        var confirmed = ThemedMessageBox.ShowConfirm(
            Window.GetWindow(this),
            $"Move \"{row.Label}\" to Trash? You can restore it from Trash later, or delete it permanently from there.",
            "Confirm Delete");

        if (!confirmed)
        {
            return;
        }

        _services.Credentials.SoftDelete(row.Id);
        _detailView.ShowEmpty();
        TryRefresh();
    }

    /// <summary>The one place a credential's data is actually erased — real removal plus its attachments/history/icon, so nothing orphaned is left on disk.</summary>
    private void PermanentlyDelete(CredentialRow row)
    {
        _services.Credentials.Delete(row.Id);
        _services.Attachments.DeleteAllForCredential(row.Id);
        _services.PasswordHistory.DeleteAllForCredential(row.Id);
        _services.CredentialIcons.DeleteAllForCredential(row.Id);
    }

    /// <summary>Permanently destroys every credential currently in Trash.</summary>
    private void OnEmptyTrashClick(object sender, RoutedEventArgs e)
    {
        var trashed = _allRows.Where(r => r.IsDeleted).ToList();
        if (trashed.Count == 0)
        {
            return;
        }

        var confirmed = ThemedMessageBox.ShowConfirm(
            Window.GetWindow(this),
            $"Permanently delete {trashed.Count} item(s) in Trash? This cannot be undone.",
            "Empty Trash Bin");

        if (!confirmed)
        {
            return;
        }

        foreach (var row in trashed)
        {
            PermanentlyDelete(row);
        }

        _detailView.ShowEmpty();
        TryRefresh();
    }

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        LockRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOptionsClick(object sender, RoutedEventArgs e)
    {
        var window = new OptionsWindow(_services) { Owner = Window.GetWindow(this) };
        window.SettingsChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        window.ShowDialog();
        UpdateSyncPanel(); // provider may have changed (enabled/migrated) while Options was open
    }

    private async void OnSyncClick(object sender, RoutedEventArgs e)
    {
        if (_services.ActiveSyncProvider is null)
        {
            ThemedMessageBox.Show(
                Window.GetWindow(this), "This vault has no cloud sync provider configured.", "Sifra");
            return;
        }

        SyncButton.IsEnabled = false;
        SyncProgressRing.Visibility = Visibility.Visible;
        SyncPanelProgressRing.Visibility = Visibility.Visible;
        UpdateSyncPanel("Syncing...");
        try
        {
            await Task.Run(() =>
            {
                _services.EnsureActiveProviderSignedIn(); // silent reconnect first, interactive browser sign-in only if that fails
                _services.CreateActiveSyncService().SyncNow();
                _services.CreateActiveAttachmentBlobSyncService().SyncBlobs();

                // Icon IMAGE bytes are local-only and never traveled through
                // sync — a credential pulled in from another device (or
                // restored fresh) can have Icon.Kind metadata claiming an
                // image that isn't actually here yet. See
                // CredentialIconService.RepairMissingImagesAsync's remarks.
                _services.CredentialIcons.RepairMissingImagesAsync(_vaultCredential).GetAwaiter().GetResult();
            });

            UpdateSyncPanel(TryRefresh((CredentialsList.SelectedItem as CredentialRow)?.Id)
                ? $"Last synced at {DateTime.Now:t}"
                : "Synced, but the list couldn't refresh — a record could not be decrypted. Try unlocking again.");
        }
        catch (CloudAuthenticationException ex)
        {
            UpdateSyncPanel("Sign-in failed.");
            ThemedMessageBox.Show(Window.GetWindow(this), $"Could not sign in: {ex.Message}", "Sifra", ThemedMessageBox.Icon.Error);
        }
        catch (SyncConflictExhaustedException ex)
        {
            UpdateSyncPanel("Sync failed — too many conflicting changes.");
            ThemedMessageBox.Show(Window.GetWindow(this), ex.Message, "Sifra", ThemedMessageBox.Icon.Warning);
        }
        catch (VaultMismatchException ex)
        {
            UpdateSyncPanel("Sync stopped — vault mismatch.");
            ThemedMessageBox.Show(
                Window.GetWindow(this), $"{ex.Message}\n\nCheck Options — this vault's configured cloud provider may be pointed at the wrong account, or a migration may be needed.",
                "Sifra", ThemedMessageBox.Icon.Error);
        }
        catch (Exception ex) when (ex is SyncUnavailableException or DropboxNetworkException or DropboxUnauthorizedException or DropboxSyncFailedException
            or GoogleDriveNetworkException or GoogleDriveUnauthorizedException or GoogleDriveSyncFailedException
            or PCloudNetworkException or PCloudUnauthorizedException or PCloudSyncFailedException)
        {
            UpdateSyncPanel("Sync failed.");
            ThemedMessageBox.Show(Window.GetWindow(this), $"Sync failed: {ex.Message}", "Sifra", ThemedMessageBox.Icon.Error);
        }
        finally
        {
            SyncButton.IsEnabled = true;
            SyncProgressRing.Visibility = Visibility.Collapsed;
            SyncPanelProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void OnGeneratorClick(object sender, RoutedEventArgs e)
    {
        var window = new GeneratorWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}
