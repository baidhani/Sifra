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
    /// Raised right after the tag registry changes (add, rename, delete,
    /// recolor, pin/unpin), so a UI already open (e.g. the Vault screen's
    /// tag sidebar, sitting behind a modal "Set Tags" dialog) can refresh
    /// immediately instead of waiting for that dialog to close.
    /// </summary>
    public event EventHandler? TagsChanged;

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
        TagsChanged?.Invoke(this, EventArgs.Empty);
        return tag;
    }

    /// <summary>
    /// Renames a tag's registry entry only — the caller is responsible for
    /// also updating the plain-string tag lists on every credential that
    /// carries the old name (see CredentialService.RenameTagEverywhere),
    /// since this store has no reference to credentials at all.
    /// </summary>
    /// <exception cref="TagNotFoundException">No tag exists with this id.</exception>
    /// <exception cref="ArgumentException">The new name is empty.</exception>
    /// <exception cref="InvalidOperationException">Another tag already has this name.</exception>
    /// <returns>The renamed tag's previous name, so the caller can cascade the rename to credentials.</returns>
    public string Rename(string id, string newName)
    {
        var trimmed = newName.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Tag name is required.", nameof(newName));
        }

        var existing = _store.GetAll();
        var tag = existing.FirstOrDefault(t => t.Id == id) ?? throw new TagNotFoundException(id);
        if (existing.Any(t => t.Id != id && string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A tag named '{trimmed}' already exists.");
        }

        var oldName = tag.Name;
        var updated = existing.Select(t => t.Id == id ? t with { Name = trimmed } : t).ToList();
        _store.Save(updated);
        TagsChanged?.Invoke(this, EventArgs.Empty);
        return oldName;
    }

    /// <summary>
    /// Removes a tag's registry entry only — the caller is responsible for
    /// also removing it from every credential's tag list (see
    /// CredentialService.RemoveTagEverywhere).
    /// </summary>
    /// <exception cref="TagNotFoundException">No tag exists with this id.</exception>
    /// <returns>The deleted tag's name, so the caller can cascade the removal to credentials.</returns>
    public string Delete(string id)
    {
        var existing = _store.GetAll();
        var tag = existing.FirstOrDefault(t => t.Id == id) ?? throw new TagNotFoundException(id);

        _store.Save(existing.Where(t => t.Id != id).ToList());
        TagsChanged?.Invoke(this, EventArgs.Empty);
        return tag.Name;
    }

    /// <exception cref="TagNotFoundException">No tag exists with this id.</exception>
    public void SetColor(string id, string color)
    {
        var existing = _store.GetAll();
        if (existing.All(t => t.Id != id))
        {
            throw new TagNotFoundException(id);
        }

        _store.Save(existing.Select(t => t.Id == id ? t with { Color = color } : t).ToList());
        TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <exception cref="TagNotFoundException">No tag exists with this id.</exception>
    public void SetPinned(string id, bool pinnedToTop)
    {
        var existing = _store.GetAll();
        if (existing.All(t => t.Id != id))
        {
            throw new TagNotFoundException(id);
        }

        _store.Save(existing.Select(t => t.Id == id ? t with { PinnedToTop = pinnedToTop } : t).ToList());
        TagsChanged?.Invoke(this, EventArgs.Empty);
    }
}
