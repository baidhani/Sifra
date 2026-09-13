using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Tests;

/// <summary>
/// Covers the fully dynamic field model: a credential is a Label plus any
/// number of typed fields (no fixed Username/Password/Url/Phone/Notes/
/// AccountNumber/Pin shape any more), plus Favorite/Tags metadata.
/// </summary>
public sealed class CredentialExtendedFieldsTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";

    private readonly string _dataDirectory;

    public CredentialExtendedFieldsTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-credential-extended-tests-" + Guid.NewGuid());
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
        new FakeCredentialClipboard());

    [Fact]
    public void Add_WithManyFieldTypes_ThenGetById_RoundTripsAllOfThem()
    {
        var service = CreateService();

        var id = service.Add(
            VaultCredential, "Bank",
            new (string, string, CustomFieldType)[]
            {
                ("Login", "firas", CustomFieldType.Login),
                ("Password", "hunter2", CustomFieldType.Password),
                ("Website", "https://bank.example.com", CustomFieldType.Website),
                ("Phone", "555-0100", CustomFieldType.Phone),
                ("Notes", "call before 5pm", CustomFieldType.Text),
                ("Account number", "ACC-12345", CustomFieldType.Text),
                ("PIN", "4321", CustomFieldType.Pin),
                ("Security Question", "Mother's maiden name", CustomFieldType.Text),
            },
            isFavorite: true, tags: new[] { "Finance", "Important" });

        var view = service.GetById(VaultCredential, id);

        Assert.Equal(new[] { "Finance", "Important" }, view.Tags);
        Assert.True(view.IsFavorite);
        Assert.Equal(8, view.Fields.Count);
        Assert.Equal("firas", view.Fields.Single(f => f.Name == "Login").Value);
        Assert.Equal("hunter2", view.Fields.Single(f => f.Name == "Password").Value);
        Assert.Equal(CustomFieldType.Phone, view.Fields.Single(f => f.Name == "Phone").Type);
        Assert.Equal("4321", view.Fields.Single(f => f.Name == "PIN").Value);
        Assert.Equal(CustomFieldType.Pin, view.Fields.Single(f => f.Name == "PIN").Type);
        var securityQuestion = view.Fields.Single(f => f.Name == "Security Question");
        Assert.Equal("Mother's maiden name", securityQuestion.Value);
        Assert.Equal(CustomFieldType.Text, securityQuestion.Type);
    }

    [Fact]
    public void Field_PreservesItsTypeAcrossEncryptAndDecrypt()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "Site",
            new[] { ("2FA Secret", "JBSWY3DPEHPK3PXP", CustomFieldType.OneTimePassword) });

        var view = service.GetById(VaultCredential, id);

        Assert.Equal(CustomFieldType.OneTimePassword, view.Fields.Single().Type);
    }

    [Fact]
    public void Add_WithNoFields_LeavesFieldsEmptyRatherThanFailing()
    {
        // Failure path guard: a credential can legitimately have zero fields
        // (e.g. a bare note under just a label) — must not throw or corrupt data.
        var service = CreateService();

        var id = service.Add(VaultCredential, "Simple Site", Array.Empty<(string, string, CustomFieldType)>());
        var view = service.GetById(VaultCredential, id);

        Assert.Empty(view.Fields);
        Assert.False(view.IsFavorite);
        Assert.Empty(view.Tags!);
    }

    [Fact]
    public void List_IncludesEveryFieldFullyDecrypted()
    {
        // With a fully dynamic field model there is no fixed "safe" field
        // left to expose without decrypting (see CredentialView's own
        // remarks) — List and GetById return the same decrypted data.
        var service = CreateService();
        service.Add(
            VaultCredential, "Bank",
            new[] { ("Recovery Code", "leaked-recovery-code", CustomFieldType.Secret) });

        var listed = service.List(VaultCredential).Single();

        Assert.Single(listed.Fields);
        Assert.Equal("leaked-recovery-code", listed.Fields[0].Value);
    }

    [Fact]
    public void List_IncludesFavoriteAndTags()
    {
        var service = CreateService();
        service.Add(VaultCredential, "Bank",
            new[] { ("Phone", "555-0100", CustomFieldType.Phone) },
            isFavorite: true, tags: new[] { "Finance" });

        var listed = service.List(VaultCredential).Single();

        Assert.Equal("555-0100", listed.Fields.Single().Value);
        Assert.True(listed.IsFavorite);
        Assert.Equal(new[] { "Finance" }, listed.Tags);
    }

    [Fact]
    public void RawStorage_NeverContainsFieldValuesAsPlaintext()
    {
        var service = CreateService();
        service.Add(
            VaultCredential, "Bank",
            new[]
            {
                ("Notes", "super-secret-note-value", CustomFieldType.Text),
                ("Account number", "ACC-77777", CustomFieldType.Text),
                ("PIN", "9999", CustomFieldType.Pin),
                ("Field", "custom-secret-value", CustomFieldType.Text),
            });

        var raw = File.ReadAllText(Path.Combine(_dataDirectory, "credentials.json"));

        Assert.DoesNotContain("super-secret-note-value", raw);
        Assert.DoesNotContain("ACC-77777", raw);
        Assert.DoesNotContain("9999", raw);
        Assert.DoesNotContain("custom-secret-value", raw);
    }

    [Fact]
    public void Edit_ReplacesTheFieldList()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "Bank", new[] { ("Phone", "555-0100", CustomFieldType.Phone) });

        service.Edit(VaultCredential, id, "Bank", new[] { ("Phone", "555-9999", CustomFieldType.Phone) }, isFavorite: true);

        var view = service.GetById(VaultCredential, id);
        Assert.Equal("555-9999", view.Fields.Single().Value);
        Assert.True(view.IsFavorite);
    }

    [Fact]
    public void SetFavorite_TogglesWithoutRequiringEveryOtherField()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "Bank", Array.Empty<(string, string, CustomFieldType)>());

        service.SetFavorite(id, true);
        Assert.True(service.GetById(VaultCredential, id).IsFavorite);

        service.SetFavorite(id, false);
        Assert.False(service.GetById(VaultCredential, id).IsFavorite);
    }

    [Fact]
    public void SetFavorite_ForANonExistentCredential_ThrowsCredentialNotFound()
    {
        var service = CreateService();

        Assert.Throws<CredentialNotFoundException>(() => service.SetFavorite("does-not-exist", true));
    }

    [Fact]
    public void SetTags_ReplacesTagsWithoutTouchingFieldsOrOtherMetadata()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "Bank", new[] { ("Phone", "555-0100", CustomFieldType.Phone) }, isFavorite: true, tags: new[] { "old" });

        service.SetTags(id, new[] { "work", "important" });

        var view = service.GetById(VaultCredential, id);
        Assert.Equal(new[] { "work", "important" }, view.Tags);
        Assert.Equal("555-0100", view.Fields.Single().Value);
        Assert.True(view.IsFavorite);
    }

    [Fact]
    public void SetTags_ForANonExistentCredential_ThrowsCredentialNotFound()
    {
        var service = CreateService();

        Assert.Throws<CredentialNotFoundException>(() => service.SetTags("does-not-exist", new[] { "work" }));
    }

    [Fact]
    public void Search_MatchesByFieldValueOrLabel()
    {
        var service = CreateService();
        service.Add(VaultCredential, "Bank",
            new[] { ("Phone", "555-0100", CustomFieldType.Phone) }, tags: new[] { "Finance" });
        service.Add(VaultCredential, "Other", new[] { ("Login", "user2", CustomFieldType.Login) });

        Assert.Single(service.Search(VaultCredential, "555-0100"));
        Assert.Single(service.Search(VaultCredential, "Finance"));
    }
}
