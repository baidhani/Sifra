using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sifra.Vault.Credentials;

namespace Sifra.Desktop;

public enum HealthLevel
{
    Checking,
    Ok,
    Weak,
    Breached,
    Unavailable,
}

/// <summary>
/// Wraps a CredentialView with a mutable health-status column for the
/// grid. The row displays immediately from the fast local list; the
/// health badge starts as "Checking..." and updates once the async
/// weakness/breach analysis (STORY-011) completes for it.
/// </summary>
public sealed class CredentialRow : INotifyPropertyChanged
{
    private string _healthStatus = "Checking...";
    private HealthLevel _healthLevel = HealthLevel.Checking;

    public CredentialRow(CredentialView view, IReadOnlyDictionary<string, string>? tagColors = null, CredentialIconService? iconService = null, bool isSessionUnlocked = false)
    {
        Id = view.Id;
        Label = view.Label;
        IsSessionUnlocked = isSessionUnlocked;
        // Dynamic field model: there is no fixed "the username field" any
        // more, so the list subtitle is the first Login-type field's value
        // if the credential has one (matching CredentialService.CopyUsername's
        // own convention), falling back to the first field of any type so a
        // credential with only e.g. a Website field still shows something.
        Subtitle = view.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Login)?.Value
            ?? view.Fields.FirstOrDefault()?.Value
            ?? string.Empty;
        UpdatedAtUtc = view.UpdatedAtUtc;
        IsFavorite = view.IsFavorite;
        IsArchived = view.IsArchived;
        IsDeleted = view.IsDeleted;
        DeletedAtUtc = view.DeletedAtUtc;
        IsLocked = view.IsLocked;
        Tags = view.Tags ?? Array.Empty<string>();
        FieldValues = view.Fields.Select(f => f.Value).ToList();

        // Icon + name per tag for the compact list row (comma-separated in
        // the XAML template) — a tag missing from the registry (e.g. added
        // via the CLI) falls back to a neutral gray icon rather than being
        // silently dropped.
        TagChips = Tags
            .Select(t => TagChipViewModel.Create(t, tagColors is not null && tagColors.TryGetValue(t, out var hex) ? hex : "#7F8C8D"))
            .ToList();

        // Same icon rendering rules as CredentialDetailView's avatar: an
        // image (favicon/custom) wins if present, else a Fluent symbol on a
        // colored circle, else the default initial-letter fallback.
        var icon = view.Icon;
        AvatarBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(icon?.BackgroundColorHex ?? "#5B8DEF"));

        if (icon?.Kind is CredentialIconKind.WebsiteFavicon or CredentialIconKind.Custom && iconService is not null)
        {
            var bytes = iconService.GetImage(view.Id);
            if (bytes is not null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    using var stream = new MemoryStream(bytes);
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    AvatarImage = bitmap;
                }
                catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
                {
                    // Failure path: a corrupted icon image falls through to the letter/symbol below rather than crashing.
                }
            }
        }

        if (AvatarImage is null && icon?.Kind == CredentialIconKind.Symbol && icon.SymbolName is not null
            && Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(icon.SymbolName, out var symbol))
        {
            AvatarSymbol = symbol;
            HasSymbol = true;
        }

        // A favicon/custom image showing means the colored circle behind it
        // must go transparent — otherwise a non-square icon (round logo,
        // transparent-background PNG) shows the fallback color bleeding
        // through its edges/corners, which the detail pane already avoided
        // by doing the same thing.
        if (AvatarImage is not null)
        {
            AvatarBackground = Brushes.Transparent;
        }
    }

    /// <summary>
    /// Set by VaultView.ApplyFilter to the current search box text — kept
    /// on the row itself (rather than passed through a converter) so
    /// LabelSegments/SubtitleSegments can recompute and raise
    /// PropertyChanged without needing the row rebuilt from CredentialView.
    /// </summary>
    private string _searchQuery = string.Empty;
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LabelSegments)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubtitleSegments)));
        }
    }

    public IReadOnlyList<TextSegment> LabelSegments => BuildSegments(Label);
    public IReadOnlyList<TextSegment> SubtitleSegments => BuildSegments(Subtitle);

    /// <summary>Splits text into match/non-match runs against SearchQuery, for the list row's highlight — a single non-match segment when there's no query or no hit.</summary>
    private IReadOnlyList<TextSegment> BuildSegments(string text)
    {
        if (_searchQuery.Length == 0 || text.Length == 0)
        {
            return [new TextSegment(text, false)];
        }

        var segments = new List<TextSegment>();
        var index = 0;
        while (index < text.Length)
        {
            var matchIndex = text.IndexOf(_searchQuery, index, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                segments.Add(new TextSegment(text[index..], false));
                break;
            }

            if (matchIndex > index)
            {
                segments.Add(new TextSegment(text[index..matchIndex], false));
            }
            segments.Add(new TextSegment(text.Substring(matchIndex, _searchQuery.Length), true));
            index = matchIndex + _searchQuery.Length;
        }

        return segments.Count > 0 ? segments : [new TextSegment(text, false)];
    }

    public string Id { get; }
    public string Label { get; }
    public string Subtitle { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public bool IsFavorite { get; }
    public bool IsArchived { get; }
    public bool IsDeleted { get; }
    public DateTimeOffset? DeletedAtUtc { get; }
    public bool IsLocked { get; }
    /// <summary>Whether this session has temporarily unlocked this item (see VaultView._sessionUnlockedIds) — passed in at construction since rows are rebuilt fresh on every Refresh.</summary>
    public bool IsSessionUnlocked { get; }
    public Visibility LockVisibility => IsLocked ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>Closed padlock while locked-and-not-session-unlocked; open padlock (still indicating "has a lock, currently open") once session-unlocked.</summary>
    public Wpf.Ui.Controls.SymbolRegular LockSymbol => IsSessionUnlocked ? Wpf.Ui.Controls.SymbolRegular.LockOpen24 : Wpf.Ui.Controls.SymbolRegular.LockClosed24;
    public IReadOnlyList<string> Tags { get; }
    /// <summary>One chip (name + color) per entry in Tags, same order — for the list row.</summary>
    public IReadOnlyList<TagChipViewModel> TagChips { get; }
    /// <summary>Every field's decrypted value, for client-side search — see VaultView's search filter.</summary>
    public IReadOnlyList<string> FieldValues { get; }
    public Visibility FavoriteVisibility => IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SubtitleVisibility => Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TagsVisibility => TagChips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string InitialLetter => Label.Length > 0 ? Label[..1].ToUpperInvariant() : "?";

    public Brush AvatarBackground { get; }
    public BitmapImage? AvatarImage { get; }
    public Wpf.Ui.Controls.SymbolRegular AvatarSymbol { get; }
    private bool HasSymbol { get; }
    public Visibility AvatarImageVisibility => AvatarImage is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AvatarSymbolVisibility => AvatarImage is null && HasSymbol ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AvatarLetterVisibility => AvatarImage is null && !HasSymbol ? Visibility.Visible : Visibility.Collapsed;

    public string TimeAgo
    {
        get
        {
            var elapsed = DateTimeOffset.UtcNow - UpdatedAtUtc;
            return elapsed switch
            {
                { TotalMinutes: < 1 } => "just now",
                { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes} min ago",
                { TotalHours: < 24 } => $"{(int)elapsed.TotalHours} hr ago",
                _ => $"{(int)elapsed.TotalDays} day{((int)elapsed.TotalDays == 1 ? "" : "s")} ago",
            };
        }
    }

    public string HealthStatus
    {
        get => _healthStatus;
        set
        {
            _healthStatus = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HealthStatus)));
        }
    }

    public HealthLevel HealthLevel
    {
        get => _healthLevel;
        set
        {
            _healthLevel = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HealthLevel)));
        }
    }

    /// <summary>
    /// Independent flags for category filtering — a credential can be weak
    /// AND reused AND breached at once, which a single HealthLevel can't
    /// represent (that's only used for the short display text/pill color).
    /// </summary>
    public bool IsWeak { get; set; }
    public bool IsReused { get; set; }
    public bool IsBreached { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One run of text for the list row's search-match highlight — IsMatch true means it should render highlighted.</summary>
public sealed record TextSegment(string Text, bool IsMatch);
