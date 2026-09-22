using Sifra.Vault.Attachments;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class AttachmentMergerTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deleted = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private static CredentialAttachment MakeAttachment() =>
        new("att-1", "cred-1", "photo.jpg", AttachmentKind.Image, 1024, Created);

    [Fact]
    public void Merge_PresentOnBothSides_KeepsIt()
    {
        var attachment = MakeAttachment();

        var result = AttachmentMerger.Merge(attachment, null, attachment, null);

        Assert.Equal(AttachmentMergeOutcome.Present, result.Outcome);
        Assert.Equal(attachment, result.Attachment);
    }

    [Fact]
    public void Merge_DeletedOnOneSideOnly_AlwaysWinsOverPresence()
    {
        // Unlike credentials, an attachment can never be "edited after
        // deletion" — it's immutable — so a tombstone always wins, no
        // timestamp race needed.
        var attachment = MakeAttachment();

        var result = AttachmentMerger.Merge(attachment, null, null, Deleted);

        Assert.Equal(AttachmentMergeOutcome.Deleted, result.Outcome);
        Assert.Equal(Deleted, result.DeletedAtUtc);
    }

    [Fact]
    public void Merge_DeletedOnBothSides_KeepsTheLaterTimestamp()
    {
        var earlier = Deleted;
        var later = Deleted.AddDays(1);

        var result = AttachmentMerger.Merge(null, earlier, null, later);

        Assert.Equal(AttachmentMergeOutcome.Deleted, result.Outcome);
        Assert.Equal(later, result.DeletedAtUtc);
    }
}
