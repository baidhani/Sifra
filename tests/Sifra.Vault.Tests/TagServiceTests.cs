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
}
