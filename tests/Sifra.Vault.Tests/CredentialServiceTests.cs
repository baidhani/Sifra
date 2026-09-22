using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using Sifra.Vault.Sync;
using static Sifra.Vault.Tests.CredentialFieldTestHelpers;

namespace Sifra.Vault.Tests;

public sealed class CredentialServiceTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;
    private readonly FakeCredentialClipboard _clipboard = new();

    public CredentialServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-credential-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private CredentialService CreateService(AuditLogger? auditLogger = null) => new(
        new CredentialStore(_dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
        _clipboard,
        auditLogger);

    [Fact]
    public void Add_ThenList_ShowsTheNewCredentialWithDecryptedFields()
    {
        // Acceptance: adding a credential makes it appear in the list.
        var service = CreateService();

        service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        var list = service.List(VaultCredential);
        Assert.Single(list);
        Assert.Equal("GitHub", list[0].Label);
        Assert.Equal("firas", list[0].Username());
        // With a fully dynamic field model there is no separate "safe" list
        // view any more (see CredentialView's own remarks) — List and
        // GetById both decrypt every field.
        Assert.Equal("hunter2", list[0].Password());
    }

    [Fact]
    public void Edit_DoesNotChangeTheCredentialsPositionInTheList()
    {
        // Regression: Upsert used to remove-then-append, which silently
        // moved every edited credential to the end of the list — confusing
        // in a long vault, since list order is meant to reflect creation
        // order, not last-edited order.
        var service = CreateService();
        var firstId = service.Add(VaultCredential, "Alpha", LoginFields("a", "pw", "https://a.example.com"));
        var middleId = service.Add(VaultCredential, "Beta", LoginFields("b", "pw", "https://b.example.com"));
        var lastId = service.Add(VaultCredential, "Gamma", LoginFields("g", "pw", "https://g.example.com"));

        service.Edit(VaultCredential, middleId, "Beta", LoginFields("b2", "pw2", "https://b.example.com"));

        var ids = service.List(VaultCredential).Select(v => v.Id).ToList();
        Assert.Equal(new[] { firstId, middleId, lastId }, ids);
    }

    [Fact]
    public void Edit_PreservesTheOriginalCreatedAtUtc_WhileUpdatedAtUtcAdvances()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        var created = service.GetById(VaultCredential, id).CreatedAtUtc;

        service.Edit(VaultCredential, id, "GitHub Renamed", LoginFields("firas", "hunter3", "https://github.com"));
        var view = service.GetById(VaultCredential, id);

        Assert.Equal(created, view.CreatedAtUtc);
        Assert.True(view.UpdatedAtUtc >= created);
    }

    [Fact]
    public void Add_EncryptsUsernameAndPasswordAtRest()
    {
        var service = CreateService();
        service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        // vault.db is a binary SQLite file (Phase 3), not JSON text — Latin1
        // maps every byte to one char without throwing, so any embedded
        // ASCII plaintext still shows up as the same substring for this
        // "never present at rest" check.
        var rawFileContents = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(_dataDirectory, "vault.db")));

        Assert.DoesNotContain("firas", rawFileContents);
        Assert.DoesNotContain("hunter2", rawFileContents);
    }

    [Fact]
    public void CopyUsername_WritesDecryptedUsernameToClipboard()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.CopyUsername(VaultCredential, id);

        Assert.Equal("firas", _clipboard.LastCopiedValue);
    }

    [Fact]
    public void CopyPassword_WritesDecryptedPasswordToClipboard()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.CopyPassword(VaultCredential, id);

        Assert.Equal("hunter2", _clipboard.LastCopiedValue);
    }

    [Fact]
    public void CopyUsername_ForNonExistentCredential_ThrowsCredentialNotFoundException()
    {
        // Failure path: "user attempts to copy a non-existent credential."
        var service = CreateService();

        Assert.Throws<CredentialNotFoundException>(() => service.CopyUsername(VaultCredential, "does-not-exist"));
        Assert.Null(_clipboard.LastCopiedValue); // nothing was written
    }

    [Fact]
    public void CopyPassword_WhenClipboardIsDenied_ThrowsClipboardUnavailableException()
    {
        // Failure path: "clipboard access is denied by the system."
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        _clipboard.SimulateFailure = true;

        Assert.Throws<ClipboardUnavailableException>(() => service.CopyPassword(VaultCredential, id));
    }

    [Fact]
    public void Duplicate_CreatesAnIndependentCopyWithCopySuffixAndResetState()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"), isFavorite: true, tags: new[] { "Work" });
        service.SetLocked(id, true);
        service.SetArchived(id, true);

        var newId = service.Duplicate(id);

        Assert.NotEqual(id, newId);
        var copy = service.GetById(VaultCredential, newId);
        Assert.Equal("GitHub (copy)", copy.Label);
        Assert.Equal("firas", copy.Username());
        Assert.Equal("hunter2", copy.Password());
        Assert.Equal(new[] { "Work" }, copy.Tags);
        // A duplicate always starts fresh — it doesn't inherit the
        // original's favorite/lock/archive state.
        Assert.False(copy.IsFavorite);
        Assert.False(copy.IsLocked);
        Assert.False(copy.IsArchived);
        // The original is untouched.
        var original = service.GetById(VaultCredential, id);
        Assert.Equal("GitHub", original.Label);
        Assert.True(original.IsLocked);
        Assert.True(original.IsArchived);
    }

    [Fact]
    public void Duplicate_ForNonExistentCredential_ThrowsCredentialNotFoundException()
    {
        // Failure path: "user attempts to duplicate a non-existent credential."
        var service = CreateService();

        Assert.Throws<CredentialNotFoundException>(() => service.Duplicate("does-not-exist"));
    }

    [Fact]
    public void CopyAsText_WritesLabelAndFieldsToClipboard()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.CopyAsText(VaultCredential, id);

        Assert.NotNull(_clipboard.LastCopiedValue);
        Assert.Contains("GitHub", _clipboard.LastCopiedValue);
        Assert.Contains("firas", _clipboard.LastCopiedValue);
        Assert.Contains("hunter2", _clipboard.LastCopiedValue);
    }

    [Fact]
    public void CopyAsText_WhenClipboardIsDenied_ThrowsClipboardUnavailableException()
    {
        // Failure path: "clipboard access is denied by the system."
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        _clipboard.SimulateFailure = true;

        Assert.Throws<ClipboardUnavailableException>(() => service.CopyAsText(VaultCredential, id));
    }

    [Fact]
    public void ExportAsText_ReturnsLabelAndFieldsWithoutTouchingTheClipboard()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        var text = service.ExportAsText(VaultCredential, id);

        Assert.Contains("GitHub", text);
        Assert.Contains("firas", text);
        Assert.Contains("hunter2", text);
        Assert.Null(_clipboard.LastCopiedValue);
    }

    [Fact]
    public void ExportAsText_ForNonExistentCredential_ThrowsCredentialNotFoundException()
    {
        // Failure path: "user attempts to export a non-existent credential."
        var service = CreateService();

        Assert.Throws<CredentialNotFoundException>(() => service.ExportAsText(VaultCredential, "does-not-exist"));
    }

    [Fact]
    public void CopyUsername_HasNoDependencyOnConnectivityAtAll()
    {
        // Failure path: "user tries to copy while offline." CredentialService
        // has no reference to ICloudAuthProvider/ICloudSyncProvider anywhere
        // in its constructor — this is structural proof, not just behavior:
        // copying cannot be affected by connectivity because nothing here
        // can observe it.
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.CopyUsername(VaultCredential, id); // no cloud object involved anywhere

        Assert.Equal("firas", _clipboard.LastCopiedValue);
    }

    [Fact]
    public void Edit_UpdatesTheCredentialAndReEncrypts()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.Edit(VaultCredential, id, "GitHub", LoginFields("firas-new", "hunter3", "https://github.com"));

        var updated = service.GetById(VaultCredential, id);
        Assert.Equal("firas-new", updated.Username());
        Assert.Equal("hunter3", updated.Password());
    }

    [Fact]
    public void Delete_RemovesTheCredentialFromTheList()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.Delete(id);

        Assert.Empty(service.List(VaultCredential));
    }

    [Fact]
    public void Delete_WithATombstoneStoreConfigured_RecordsATombstone()
    {
        // Phase 3: without a tombstone, a device that synced before this
        // delete would push its still-locally-present copy back up and
        // silently resurrect it on its next sync.
        var tombstones = new CredentialTombstoneStore(_dataDirectory);
        var service = new CredentialService(
            new CredentialStore(_dataDirectory), new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
            _clipboard, tombstones: tombstones);
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.Delete(id);

        Assert.NotNull(tombstones.Load(id));
    }

    [Fact]
    public void Delete_WithNoTombstoneStoreConfigured_NeverAttemptsToRecordOne()
    {
        // Existing/local-only vaults (the default) must behave exactly as before.
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.Delete(id); // must not throw for lack of a tombstone store

        Assert.Empty(service.List(VaultCredential));
    }

    [Fact]
    public void Search_FindsByFieldValueOrPlaintextLabel()
    {
        var service = CreateService();
        service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        service.Add(VaultCredential, "Gmail", LoginFields("firas.other@example.com", "hunter3", "https://gmail.com"));

        var byLabel = service.Search(VaultCredential, "git");
        var byUsername = service.Search(VaultCredential, "other@example");

        Assert.Single(byLabel);
        Assert.Equal("GitHub", byLabel[0].Label);
        Assert.Single(byUsername);
        Assert.Equal("Gmail", byUsername[0].Label);
    }

    [Fact]
    public void Add_WithAuditLoggerProvided_LogsTheOperationWithoutLoggingUsernameOrPassword()
    {
        // Trust: all actions are logged with a timestamp and user ID —
        // reusing STORY-015's AuditLogger — and secrets never appear in the log.
        var sink = new FileAuditLogSink(Path.Combine(_dataDirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());
        var service = CreateService(logger);

        // Deliberately distinct from Environment.UserName (which the audit
        // log legitimately includes) so this assertion can't collide with it.
        service.Add(VaultCredential, "GitHub", LoginFields("credential-username-should-not-leak", "hunter2", "https://github.com"));

        var lines = sink.ReadAll();
        Assert.Single(lines);
        Assert.Contains("Add", lines[0]);
        Assert.DoesNotContain("credential-username-should-not-leak", lines[0]);
        Assert.DoesNotContain("hunter2", lines[0]);
    }
}
