using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using static Sifra.Vault.Tests.CredentialFieldTestHelpers;

namespace Sifra.Vault.Tests;

public sealed class CredentialArchiveTrashTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;
    private readonly FakeCredentialClipboard _clipboard = new();

    public CredentialArchiveTrashTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-credential-archive-trash-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private CredentialService CreateService() => new(
        new CredentialStore(_dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
        _clipboard);

    [Fact]
    public void SetArchived_True_MarksTheCredentialArchivedWithoutTouchingItsData()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.SetArchived(id, true);

        var view = service.GetById(VaultCredential, id);
        Assert.True(view.IsArchived);
        Assert.Equal("GitHub", view.Label);
        Assert.Equal(3, view.Fields.Count);
    }

    [Fact]
    public void SetArchived_False_UnarchivesIt()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        service.SetArchived(id, true);

        service.SetArchived(id, false);

        Assert.False(service.GetById(VaultCredential, id).IsArchived);
    }

    [Fact]
    public void SoftDelete_MarksDeletedAndStampsDeletedAtUtc_ButKeepsTheRecordFullyIntact()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        var before = DateTimeOffset.UtcNow;

        service.SoftDelete(id);

        var view = service.GetById(VaultCredential, id);
        Assert.True(view.IsDeleted);
        Assert.NotNull(view.DeletedAtUtc);
        Assert.True(view.DeletedAtUtc >= before);
        Assert.Equal("GitHub", view.Label);
        Assert.Equal(3, view.Fields.Count);
        // Still findable by GetById/List — soft delete never removes the row.
        Assert.Single(service.List(VaultCredential), v => v.Id == id);
    }

    [Fact]
    public void Restore_ClearsIsDeletedAndDeletedAtUtc()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        service.SoftDelete(id);

        service.Restore(id);

        var view = service.GetById(VaultCredential, id);
        Assert.False(view.IsDeleted);
        Assert.Null(view.DeletedAtUtc);
    }

    [Fact]
    public void Delete_PermanentlyRemovesTheCredential_UnlikeSoftDelete()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.Delete(id);

        Assert.Empty(service.List(VaultCredential));
        Assert.Throws<CredentialNotFoundException>(() => service.GetById(VaultCredential, id));
    }

    [Fact]
    public void Edit_PreservesArchivedAndDeletedState()
    {
        // Regression: Edit rebuilds the whole record — it must carry
        // IsArchived/IsDeleted/DeletedAtUtc forward, the same way it
        // already carries Icon forward, otherwise any unrelated edit
        // would silently un-delete/un-archive the credential.
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        service.SetArchived(id, true);
        service.SoftDelete(id);

        service.Edit(VaultCredential, id, "GitHub Renamed", LoginFields("firas", "newpass", "https://github.com"));

        var view = service.GetById(VaultCredential, id);
        Assert.True(view.IsArchived);
        Assert.True(view.IsDeleted);
        Assert.NotNull(view.DeletedAtUtc);
    }

    [Fact]
    public void NewCredential_IsNotArchivedOrDeletedByDefault()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        var view = service.GetById(VaultCredential, id);
        Assert.False(view.IsArchived);
        Assert.False(view.IsDeleted);
        Assert.Null(view.DeletedAtUtc);
        Assert.False(view.IsLocked);
    }

    [Fact]
    public void SetLocked_True_MarksTheCredentialLockedWithoutTouchingItsData()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        service.SetLocked(id, true);

        var view = service.GetById(VaultCredential, id);
        Assert.True(view.IsLocked);
        Assert.Equal("GitHub", view.Label);
        Assert.Equal(3, view.Fields.Count);
    }

    [Fact]
    public void SetLocked_False_Unlocks()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        service.SetLocked(id, true);

        service.SetLocked(id, false);

        Assert.False(service.GetById(VaultCredential, id).IsLocked);
    }

    [Fact]
    public void Edit_PreservesLockedState()
    {
        // Regression: Edit must carry IsLocked forward the same way it
        // already carries Icon/IsArchived/IsDeleted forward.
        var service = CreateService();
        var id = service.Add(VaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        service.SetLocked(id, true);

        service.Edit(VaultCredential, id, "GitHub Renamed", LoginFields("firas", "newpass", "https://github.com"));

        Assert.True(service.GetById(VaultCredential, id).IsLocked);
    }
}
