using Sifra.Vault.Credentials;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

public sealed class CredentialTombstoneMergerTests
{
    private static readonly DateTimeOffset LastMonth = new(2025, 12, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = new(2026, 1, 6, 14, 0, 0, TimeSpan.Zero);

    private static CustomField Field(string name, string value, DateTimeOffset updatedAt) => new(name, value, CustomFieldType.Text, updatedAt);

    private static Credential MakeCredential(DateTimeOffset updatedAt) =>
        new("cred-1", "GitHub", [Field("Username", "firas", updatedAt)], updatedAt, LastMonth);

    [Fact]
    public void Merge_BothSidesAlive_DefersToFieldLevelMerge()
    {
        var local = MakeCredential(Monday);
        var remote = MakeCredential(Tuesday);

        var result = CredentialTombstoneMerger.Merge(local, null, remote, null);

        Assert.Equal(CredentialMergeOutcome.Present, result.Outcome);
        Assert.NotNull(result.Credential);
        Assert.Null(result.DeletedAtUtc);
    }

    [Fact]
    public void Merge_DeletedRemotelyAfterLocalsLastEdit_StaysDeleted()
    {
        // The exact scenario the tombstone exists to prevent: local device
        // was offline since before the remote deletion and still has the
        // credential locally, unedited since before the delete.
        var local = MakeCredential(LastMonth);

        var result = CredentialTombstoneMerger.Merge(local, null, remoteCredential: null, remoteDeletedAtUtc: Monday);

        Assert.Equal(CredentialMergeOutcome.Deleted, result.Outcome);
        Assert.Null(result.Credential);
        Assert.Equal(Monday, result.DeletedAtUtc);
    }

    [Fact]
    public void Merge_EditedLocallyAfterRemoteDeletion_Resurrects()
    {
        // The dispute case: local edited it AFTER the remote deletion —
        // the more recent signal (an intentional edit) wins over the
        // older deletion, per the same last-write-wins rule as everything else.
        var local = MakeCredential(Tuesday);

        var result = CredentialTombstoneMerger.Merge(local, null, remoteCredential: null, remoteDeletedAtUtc: Monday);

        Assert.Equal(CredentialMergeOutcome.Present, result.Outcome);
        Assert.Equal(local, result.Credential);
    }

    [Fact]
    public void Merge_BothSidesDeleted_StaysDeletedWithTheLaterTimestamp()
    {
        var result = CredentialTombstoneMerger.Merge(null, Monday, null, Tuesday);

        Assert.Equal(CredentialMergeOutcome.Deleted, result.Outcome);
        Assert.Equal(Tuesday, result.DeletedAtUtc);
    }

    [Fact]
    public void Merge_DeletedLocallyAliveRemotelyOlder_StaysDeleted()
    {
        var remote = MakeCredential(LastMonth);

        var result = CredentialTombstoneMerger.Merge(null, Monday, remote, null);

        Assert.Equal(CredentialMergeOutcome.Deleted, result.Outcome);
    }

    [Fact]
    public void Merge_DeletedLocallyButRemoteEditedAfter_Resurrects()
    {
        var remote = MakeCredential(Tuesday);

        var result = CredentialTombstoneMerger.Merge(null, Monday, remote, null);

        Assert.Equal(CredentialMergeOutcome.Present, result.Outcome);
        Assert.Equal(remote, result.Credential);
    }

    [Fact]
    public void Merge_NeitherSideSuppliesCredentialOrTombstone_Throws()
    {
        Assert.Throws<ArgumentException>(() => CredentialTombstoneMerger.Merge(null, null, MakeCredential(Monday), null));
        Assert.Throws<ArgumentException>(() => CredentialTombstoneMerger.Merge(MakeCredential(Monday), null, null, null));
    }
}
