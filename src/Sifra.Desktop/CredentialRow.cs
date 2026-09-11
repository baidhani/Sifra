using System.ComponentModel;
using System.Windows;
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

    public CredentialRow(CredentialView view)
    {
        Id = view.Id;
        Label = view.Label;
        Username = view.Username;
        Url = view.Url;
        UpdatedAtUtc = view.UpdatedAtUtc;
        IsFavorite = view.IsFavorite;
        Labels = view.Labels ?? Array.Empty<string>();
    }

    public string Id { get; }
    public string Label { get; }
    public string Username { get; }
    public string? Url { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public bool IsFavorite { get; }
    public IReadOnlyList<string> Labels { get; }
    public Visibility FavoriteVisibility => IsFavorite ? Visibility.Visible : Visibility.Collapsed;

    public string InitialLetter => Label.Length > 0 ? Label[..1].ToUpperInvariant() : "?";

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
