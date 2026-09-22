using Sifra.Vault.Credentials;
using Sifra.Vault.Sync;

namespace Sifra.Vault.Tests;

/// <summary>
/// Pure data-level tests: CredentialMerger never touches ciphertext or the
/// vault master key, so these build Credential/CustomField records
/// directly rather than going through CredentialService — the
/// "EncryptedValueBase64" strings below are just opaque tokens for these
/// tests, standing in for whatever ciphertext would really be there.
/// </summary>
public sealed class CredentialMergerTests
{
    private static readonly DateTimeOffset Monday = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MondayLater = new(2026, 1, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = new(2026, 1, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastMonth = new(2025, 12, 1, 9, 0, 0, TimeSpan.Zero);

    private static CustomField Field(string name, string value, DateTimeOffset updatedAt, CustomFieldType type = CustomFieldType.Text) =>
        new(name, value, type, updatedAt);

    private static Credential MakeCredential(
        string id, string label, IReadOnlyList<CustomField> fields, DateTimeOffset updatedAt,
        DateTimeOffset? createdAt = null, bool isFavorite = false, IReadOnlyList<string>? tags = null) =>
        new(id, label, fields, updatedAt, createdAt ?? LastMonth, isFavorite, tags);

    [Fact]
    public void Merge_TwoDisjointFieldEdits_KeepsBothWithTheirOwnTimestamps()
    {
        // The exact "Monday username, Tuesday password" scenario: two
        // separate fields edited at two separate times must both survive
        // a merge against a stale remote copy that saw neither edit.
        var local = MakeCredential(
            "cred-1", "GitHub",
            [Field("Username", "firas-new", Monday), Field("Password", "hunter3", Tuesday, CustomFieldType.Password)],
            updatedAt: Tuesday);
        var remote = MakeCredential(
            "cred-1", "GitHub",
            [Field("Username", "firas", LastMonth), Field("Password", "hunter2", LastMonth, CustomFieldType.Password)],
            updatedAt: LastMonth);

        var merged = CredentialMerger.Merge(local, remote);

        Assert.Equal("firas-new", merged.Fields.Single(f => f.Name == "Username").EncryptedValueBase64);
        Assert.Equal("hunter3", merged.Fields.Single(f => f.Name == "Password").EncryptedValueBase64);
    }

    [Fact]
    public void Merge_ConflictingEditsToTheSameField_NewerFieldTimestampWins()
    {
        var local = MakeCredential("cred-1", "GitHub", [Field("Username", "local-value", Monday)], updatedAt: Monday);
        var remote = MakeCredential("cred-1", "GitHub", [Field("Username", "remote-value", MondayLater)], updatedAt: MondayLater);

        var merged = CredentialMerger.Merge(local, remote);

        Assert.Equal("remote-value", merged.Fields.Single().EncryptedValueBase64);
    }

    [Fact]
    public void Merge_DifferentFieldsEditedOnDifferentDevices_KeepsEachDevicesNewerField()
    {
        // The scenario from the design discussion: Device A's Username edit
        // (newer than remote's last full state) and Device B's Password
        // edit (newer than local's last full state) both survive — a true
        // per-field merge, not "whichever record is newer wins outright."
        var local = MakeCredential(
            "cred-1", "GitHub",
            [Field("Username", "local-username", Monday), Field("Password", "old-password", LastMonth, CustomFieldType.Password)],
            updatedAt: Monday);
        var remote = MakeCredential(
            "cred-1", "GitHub",
            [Field("Username", "old-username", LastMonth), Field("Password", "remote-password", Tuesday, CustomFieldType.Password)],
            updatedAt: Tuesday);

        var merged = CredentialMerger.Merge(local, remote);

        Assert.Equal("local-username", merged.Fields.Single(f => f.Name == "Username").EncryptedValueBase64);
        Assert.Equal("remote-password", merged.Fields.Single(f => f.Name == "Password").EncryptedValueBase64);
    }

    [Fact]
    public void Merge_FieldAddedLocallyAfterRemotesLastEdit_IsKept()
    {
        var local = MakeCredential(
            "cred-1", "GitHub",
            [Field("Username", "firas", LastMonth), Field("Notes", "new note", Tuesday)],
            updatedAt: Tuesday);
        var remote = MakeCredential("cred-1", "GitHub", [Field("Username", "firas", LastMonth)], updatedAt: LastMonth);

        var merged = CredentialMerger.Merge(local, remote);

        Assert.Contains(merged.Fields, f => f.Name == "Notes");
    }

    [Fact]
    public void Merge_FieldDeletedRemotelyAfterLocalLastSawIt_IsDropped()
    {
        // Local still has "Notes" from before it last synced; remote's last
        // full rewrite (Tuesday) is newer than local ever saw that field
        // (LastMonth) and no longer has it — the deletion wins.
        var local = MakeCredential(
            "cred-1", "GitHub",
            [Field("Username", "firas", LastMonth), Field("Notes", "old note", LastMonth)],
            updatedAt: LastMonth);
        var remote = MakeCredential("cred-1", "GitHub", [Field("Username", "firas", LastMonth)], updatedAt: Tuesday);

        var merged = CredentialMerger.Merge(local, remote);

        Assert.DoesNotContain(merged.Fields, f => f.Name == "Notes");
    }

    [Fact]
    public void Merge_FieldAddedLocallyAfterRemoteDeletedItElsewhere_StillKeptBecauseTheAddIsNewer()
    {
        // Distinguishes "field missing because never synced yet" from
        // "field missing because deleted": here local's add of "Notes"
        // (Tuesday) is NEWER than remote's last full state (Monday), so it
        // is a genuine re-add/edit, not a stale ghost of a deleted field.
        var local = MakeCredential(
            "cred-1", "GitHub",
            [Field("Username", "firas", LastMonth), Field("Notes", "re-added note", Tuesday)],
            updatedAt: Tuesday);
        var remote = MakeCredential("cred-1", "GitHub", [Field("Username", "firas", LastMonth)], updatedAt: Monday);

        var merged = CredentialMerger.Merge(local, remote);

        Assert.Equal("re-added note", merged.Fields.Single(f => f.Name == "Notes").EncryptedValueBase64);
    }

    [Fact]
    public void Merge_MetadataOutsideFields_FollowsWholeRecordLastWriteWins()
    {
        // Label/IsFavorite/Tags have no finer-grained timestamp than the
        // credential's own UpdatedAtUtc, so the newer whole record supplies
        // all of it.
        var local = MakeCredential("cred-1", "GitHub (local label)", [Field("Username", "firas", Monday)], updatedAt: Monday, isFavorite: true, tags: ["work"]);
        var remote = MakeCredential("cred-1", "GitHub (remote label)", [Field("Username", "firas", Monday)], updatedAt: Tuesday, isFavorite: false, tags: ["personal"]);

        var merged = CredentialMerger.Merge(local, remote);

        Assert.Equal("GitHub (remote label)", merged.Label);
        Assert.False(merged.IsFavorite);
        Assert.Equal(["personal"], merged.Tags);
    }

    [Fact]
    public void Merge_CreatedAtUtc_AlwaysKeepsTheEarlierOfTheTwo()
    {
        var local = MakeCredential("cred-1", "GitHub", [Field("Username", "firas", Monday)], updatedAt: Monday, createdAt: LastMonth);
        var remote = MakeCredential("cred-1", "GitHub", [Field("Username", "firas", Monday)], updatedAt: Tuesday, createdAt: Tuesday);

        var merged = CredentialMerger.Merge(local, remote);

        Assert.Equal(LastMonth, merged.CreatedAtUtc);
    }

    [Fact]
    public void Merge_IdenticalRecords_IsIdempotent()
    {
        var credential = MakeCredential("cred-1", "GitHub", [Field("Username", "firas", Monday)], updatedAt: Monday);

        var merged = CredentialMerger.Merge(credential, credential);

        // Credential's auto-generated equality compares Fields by List
        // reference, not by value, so compare the merge's actual outputs
        // directly rather than via record Equals.
        Assert.Equal(credential.Label, merged.Label);
        Assert.Equal(credential.UpdatedAtUtc, merged.UpdatedAtUtc);
        Assert.Equal(credential.CreatedAtUtc, merged.CreatedAtUtc);
        Assert.Equal(credential.Fields, merged.Fields);
    }

    [Fact]
    public void Merge_DifferentIds_ThrowsRatherThanSilentlyMergingUnrelatedCredentials()
    {
        var local = MakeCredential("cred-1", "GitHub", [Field("Username", "firas", Monday)], updatedAt: Monday);
        var remote = MakeCredential("cred-2", "Gmail", [Field("Username", "firas", Monday)], updatedAt: Monday);

        Assert.Throws<ArgumentException>(() => CredentialMerger.Merge(local, remote));
    }
}
