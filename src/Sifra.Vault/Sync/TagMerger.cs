using Sifra.Vault.Tags;

namespace Sifra.Vault.Sync;

/// <summary>
/// Merges the shared tag registry: a plain union by Id. Unlike credentials,
/// TagService today only ever adds tags — there is no rename/recolor/delete
/// API yet — so two devices cannot actually produce a genuine conflicting
/// edit to the SAME tag; any given Id only ever has one definition anywhere,
/// making per-field last-write-wins unnecessary for now. If tag editing is
/// added later, this needs the same UpdatedAtUtc-based approach as
/// CredentialMerger — revisit this class at that point.
/// </summary>
public static class TagMerger
{
    public static List<TagDefinition> Merge(IReadOnlyList<TagDefinition> local, IReadOnlyList<TagDefinition> remote)
    {
        var byId = new Dictionary<string, TagDefinition>();
        foreach (var tag in remote)
        {
            byId[tag.Id] = tag;
        }

        // Local wins any (currently impossible, since Ids are GUIDs) Id
        // collision — arbitrary but deterministic, and irrelevant until
        // tag editing exists.
        foreach (var tag in local)
        {
            byId[tag.Id] = tag;
        }

        return byId.Values.ToList();
    }
}
