using Sifra.Vault.Tags;

namespace Sifra.Vault.Tests;

public sealed class TagServiceTests : IDisposable
{
    private readonly string _dataDirectory;

    public TagServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-tag-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private TagService CreateService() => new(new TagStore(_dataDirectory));

    [Fact]
    public void Add_ThenList_ReturnsTheNewTag()
    {
        var service = CreateService();

        service.Add("Bank", "#5B8DEF", pinnedToTop: false);
        var tags = service.List();

        var tag = Assert.Single(tags);
        Assert.Equal("Bank", tag.Name);
        Assert.Equal("#5B8DEF", tag.Color);
        Assert.False(tag.PinnedToTop);
    }

    [Fact]
    public void List_SortsPinnedTagsFirst_ThenAlphabetically()
    {
        var service = CreateService();
        service.Add("Zebra", "#000000", pinnedToTop: false);
        service.Add("Important", "#ff0000", pinnedToTop: true);
        service.Add("Alpha", "#00ff00", pinnedToTop: false);

        var names = service.List().Select(t => t.Name).ToList();

        Assert.Equal(new[] { "Important", "Alpha", "Zebra" }, names);
    }

    [Fact]
    public void Add_WithDuplicateName_ThrowsInvalidOperationException()
    {
        var service = CreateService();
        service.Add("Bank", "#5B8DEF", pinnedToTop: false);

        Assert.Throws<InvalidOperationException>(() => service.Add("bank", "#000000", pinnedToTop: false));
    }

    [Fact]
    public void Add_WithEmptyName_ThrowsArgumentException()
    {
        var service = CreateService();

        Assert.Throws<ArgumentException>(() => service.Add("   ", "#5B8DEF", pinnedToTop: false));
    }

    [Fact]
    public void Rename_ChangesTheNameAndReturnsTheOldOne()
    {
        var service = CreateService();
        var tag = service.Add("Bank", "#5B8DEF", pinnedToTop: false);

        var oldName = service.Rename(tag.Id, "Finance");

        Assert.Equal("Bank", oldName);
        Assert.Equal("Finance", Assert.Single(service.List()).Name);
    }

    [Fact]
    public void Rename_ToAnExistingTagsName_ThrowsInvalidOperationException()
    {
        // Failure path: "user renames a tag to a name that's already taken."
        var service = CreateService();
        service.Add("Bank", "#5B8DEF", pinnedToTop: false);
        var tag = service.Add("Work", "#000000", pinnedToTop: false);

        Assert.Throws<InvalidOperationException>(() => service.Rename(tag.Id, "bank"));
    }

    [Fact]
    public void Rename_ForNonExistentTag_ThrowsTagNotFoundException()
    {
        // Failure path: "user attempts to rename a non-existent tag."
        var service = CreateService();

        Assert.Throws<TagNotFoundException>(() => service.Rename("does-not-exist", "New Name"));
    }

    [Fact]
    public void Delete_RemovesTheTagAndReturnsItsName()
    {
        var service = CreateService();
        var tag = service.Add("Bank", "#5B8DEF", pinnedToTop: false);

        var deletedName = service.Delete(tag.Id);

        Assert.Equal("Bank", deletedName);
        Assert.Empty(service.List());
    }

    [Fact]
    public void Delete_ForNonExistentTag_ThrowsTagNotFoundException()
    {
        // Failure path: "user attempts to delete a non-existent tag."
        var service = CreateService();

        Assert.Throws<TagNotFoundException>(() => service.Delete("does-not-exist"));
    }

    [Fact]
    public void SetColor_ChangesOnlyTheColor()
    {
        var service = CreateService();
        var tag = service.Add("Bank", "#5B8DEF", pinnedToTop: true);

        service.SetColor(tag.Id, "#E74C3C");

        var updated = Assert.Single(service.List());
        Assert.Equal("#E74C3C", updated.Color);
        Assert.Equal("Bank", updated.Name);
        Assert.True(updated.PinnedToTop);
    }

    [Fact]
    public void SetColor_ForNonExistentTag_ThrowsTagNotFoundException()
    {
        // Failure path: "user attempts to recolor a non-existent tag."
        var service = CreateService();

        Assert.Throws<TagNotFoundException>(() => service.SetColor("does-not-exist", "#E74C3C"));
    }

    [Fact]
    public void SetPinned_MovesTheTagToTheFrontOfTheList()
    {
        var service = CreateService();
        service.Add("Zebra", "#000000", pinnedToTop: false);
        var alpha = service.Add("Alpha", "#00ff00", pinnedToTop: false);

        service.SetPinned(alpha.Id, true);

        Assert.Equal("Alpha", service.List()[0].Name);
    }

    [Fact]
    public void SetPinned_ForNonExistentTag_ThrowsTagNotFoundException()
    {
        // Failure path: "user attempts to pin a non-existent tag."
        var service = CreateService();

        Assert.Throws<TagNotFoundException>(() => service.SetPinned("does-not-exist", true));
    }

    [Fact]
    public void MutatingMethods_RaiseTagsChanged()
    {
        var service = CreateService();
        var raisedCount = 0;
        service.TagsChanged += (_, _) => raisedCount++;

        var tag = service.Add("Bank", "#5B8DEF", pinnedToTop: false);
        service.Rename(tag.Id, "Finance");
        service.SetColor(tag.Id, "#000000");
        service.SetPinned(tag.Id, true);
        service.Delete(tag.Id);

        Assert.Equal(5, raisedCount);
    }
}
