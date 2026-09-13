namespace Sifra.Vault.Tags;

/// <summary>
/// The shared tag registry credentials draw from when tagged via
/// Credential.Tags. Pinned tags sort first, then alphabetically —
/// matching the "Set Tags" picker's expected order.
/// </summary>
public sealed class TagService
{
    private readonly TagStore _store;

    public TagService(TagStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Raised right after a new tag is persisted, so a UI already open
    /// (e.g. the Vault screen's tag sidebar, sitting behind a modal "Set
    /// Tags" dialog) can refresh immediately instead of waiting for that
    /// dialog to close.
    /// </summary>
    public event EventHandler? TagAdded;

    public IReadOnlyList<TagDefinition> List() =>
        _store.GetAll()
            .OrderByDescending(t => t.PinnedToTop)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public TagDefinition Add(string name, string color, bool pinnedToTop)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Tag name is required.", nameof(name));
        }

        var existing = _store.GetAll();
        if (existing.Any(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A tag named '{trimmed}' already exists.");
        }

        var tag = new TagDefinition(Guid.NewGuid().ToString("N"), trimmed, color, pinnedToTop);
        _store.Save(existing.Append(tag).ToList());
        TagAdded?.Invoke(this, EventArgs.Empty);
        return tag;
    }
}
