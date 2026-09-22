using Sifra.Vault.Sync;
using Sifra.Vault.Tags;

namespace Sifra.Vault.Tests;

public sealed class TagMergerTests
{
    [Fact]
    public void Merge_DisjointTags_ReturnsTheUnion()
    {
        var local = new List<TagDefinition> { new("id-1", "Work", "#ff0000", false) };
        var remote = new List<TagDefinition> { new("id-2", "Personal", "#00ff00", false) };

        var merged = TagMerger.Merge(local, remote);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, t => t.Id == "id-1");
        Assert.Contains(merged, t => t.Id == "id-2");
    }

    [Fact]
    public void Merge_SameTagOnBothSides_ReturnsItOnce()
    {
        var tag = new TagDefinition("id-1", "Work", "#ff0000", false);

        var merged = TagMerger.Merge([tag], [tag]);

        Assert.Single(merged);
    }

    [Fact]
    public void Merge_EmptyLocal_ReturnsAllRemoteTags()
    {
        var remote = new List<TagDefinition> { new("id-1", "Work", "#ff0000", false) };

        var merged = TagMerger.Merge([], remote);

        Assert.Single(merged);
    }
}
