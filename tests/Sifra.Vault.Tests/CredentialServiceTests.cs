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
    public void RenameTagEverywhere_UpdatesTheTagOnEveryCarryingCredential()
    {
        var service = CreateService();
        var workId = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"), tags: new[] { "Work", "Dev" });
        var personalId = service.Add(VaultCredential, "Netflix", LoginFields("firas", "pw", "https://netflix.com"), tags: new[] { "Personal" });

        var count = service.RenameTagEverywhere("Work", "Job");

        Assert.Equal(1, count);
        Assert.Equal(new[] { "Job", "Dev" }, service.GetById(VaultCredential, workId).Tags);
        Assert.Equal(new[] { "Personal" }, service.GetById(VaultCredential, personalId).Tags);
    }

    [Fact]
    public void RenameTagEverywhere_WhenNoCredentialCarriesTheTag_ReturnsZeroAndChangesNothing()
    {
        // Failure/edge path: "the tag being renamed isn't actually used anywhere."
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"), tags: new[] { "Dev" });

        var count = service.RenameTagEverywhere("Work", "Job");

        Assert.Equal(0, count);
        Assert.Equal(new[] { "Dev" }, service.GetById(VaultCredential, id).Tags);
    }

    [Fact]
    public void RemoveTagEverywhere_RemovesOnlyThatTagFromEveryCarryingCredential()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"), tags: new[] { "Work", "Dev" });

        var count = service.RemoveTagEverywhere("Work");

        Assert.Equal(1, count);
        Assert.Equal(new[] { "Dev" }, service.GetById(VaultCredential, id).Tags);
    }

    [Fact]
    public void ExportByTag_PlainText_IncludesOnlyMatchingCredentials()
    {
        var service = CreateService();
        service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"), tags: new[] { "Work" });
        service.Add(VaultCredential, "Netflix", LoginFields("firas", "pw", "https://netflix.com"), tags: new[] { "Personal" });

        var text = service.ExportByTag(VaultCredential, "Work", CredentialExportFormat.PlainText);

        Assert.Contains("GitHub", text);
        Assert.DoesNotContain("Netflix", text);
    }

    [Fact]
    public void ExportByTag_Csv_HasAHeaderRowAndOneDataRowPerCredential()
    {
        var service = CreateService();
        service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"), tags: new[] { "Work" });

        var csv = service.ExportByTag(VaultCredential, "Work", CredentialExportFormat.Csv);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Label,Username,Password,Website", lines[0]);
        Assert.Equal("GitHub,firas,hunter2,https://github.com", lines[1]);
    }

    [Fact]
    public void ExportByTag_Csv_EscapesValuesContainingCommas()
    {
        // Failure/edge path: "a field value itself contains a comma."
        var service = CreateService();
        service.Add(VaultCredential, "Note", [("Notes", "a, b, c", CustomFieldType.Text)], tags: new[] { "Work" });

        var csv = service.ExportByTag(VaultCredential, "Work", CredentialExportFormat.Csv);

        Assert.Contains("\"a, b, c\"", csv);
    }

    [Fact]
    public void ExportByTag_Xml_ProducesOneCredentialElementPerMatch()
    {
        var service = CreateService();
        service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"), tags: new[] { "Work" });

        var xml = service.ExportByTag(VaultCredential, "Work", CredentialExportFormat.Xml);
        var doc = System.Xml.Linq.XDocument.Parse(xml);

        var credentialElement = Assert.Single(doc.Root!.Elements("Credential"));
        Assert.Equal("GitHub", credentialElement.Element("Label")!.Value);
    }

    [Fact]
    public void ExportByTag_ForATagNoCredentialCarries_ReturnsAnEmptyExport()
    {
        // Failure/edge path: "the tag has no matching credentials."
        var service = CreateService();
        service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"), tags: new[] { "Dev" });

        var text = service.ExportByTag(VaultCredential, "DoesNotExist", CredentialExportFormat.PlainText);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ExportAllTagged_IncludesEveryCredentialWithAtLeastOneTag()
    {
        var service = CreateService();
        service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"), tags: new[] { "Work" });
        service.Add(VaultCredential, "Netflix", LoginFields("firas", "pw", "https://netflix.com"), tags: new[] { "Personal" });
        service.Add(VaultCredential, "Untagged", LoginFields("firas", "pw2"));

        var text = service.ExportAllTagged(VaultCredential, CredentialExportFormat.PlainText);

        Assert.Contains("GitHub", text);
        Assert.Contains("Netflix", text);
        Assert.DoesNotContain("Untagged", text);
    }

    [Fact]
    public void ExportAllTagged_WhenNoCredentialHasATag_ReturnsAnEmptyExport()
    {
        // Failure/edge path: "no credential in the vault has any tag at all."
        var service = CreateService();
        service.Add(VaultCredential, "Untagged", LoginFields("firas", "pw"));

        var text = service.ExportAllTagged(VaultCredential, CredentialExportFormat.PlainText);

        Assert.Equal(string.Empty, text);
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
