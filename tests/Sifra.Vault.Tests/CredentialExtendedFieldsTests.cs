using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;

namespace Sifra.Vault.Tests;

/// <summary>Covers the extended fields added on top of STORY-002's original Label/Username/Password/Url model.</summary>
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
    public void Add_WithExtendedFields_ThenGetById_RoundTripsAllOfThem()
    {
        var service = CreateService();

        var id = service.Add(
            VaultCredential, "Bank", "firas", "hunter2", "https://bank.example.com",
            phone: "555-0100", notes: "call before 5pm", accountNumber: "ACC-12345", pin: "4321",
            customFields: new[] { ("Security Question", "Mother's maiden name", CustomFieldType.Text) },
            isFavorite: true, labels: new[] { "Finance", "Important" });

        var view = service.GetById(VaultCredential, id);

        Assert.Equal("555-0100", view.Phone);
        Assert.Equal("call before 5pm", view.Notes);
        Assert.Equal("ACC-12345", view.AccountNumber);
        Assert.Equal("4321", view.Pin);
        Assert.Equal(new[] { "Finance", "Important" }, view.Labels);
        Assert.True(view.IsFavorite);
        Assert.Single(view.CustomFields!);
        Assert.Equal("Security Question", view.CustomFields![0].Name);
        Assert.Equal("Mother's maiden name", view.CustomFields[0].Value);
        Assert.Equal(CustomFieldType.Text, view.CustomFields[0].Type);
    }

    [Fact]
    public void CustomField_PreservesItsTypeAcrossEncryptAndDecrypt()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "Site", "user", "pass", null,
            customFields: new[] { ("2FA Secret", "JBSWY3DPEHPK3PXP", CustomFieldType.OneTimePassword) });

        var view = service.GetById(VaultCredential, id);

        Assert.Equal(CustomFieldType.OneTimePassword, view.CustomFields!.Single().Type);
    }

    [Fact]
    public void Add_WithoutExtendedFields_LeavesThemNullOrEmptyRatherThanFailing()
    {
        // Failure path guard: omitting the new optional fields must behave
        // exactly like the original STORY-002 API, not throw or corrupt data.
        var service = CreateService();

        var id = service.Add(VaultCredential, "Simple Site", "user", "pass", null);
        var view = service.GetById(VaultCredential, id);

        Assert.Null(view.Phone);
        Assert.Null(view.Notes);
        Assert.Null(view.AccountNumber);
        Assert.Null(view.Pin);
        Assert.Empty(view.CustomFields!);
        Assert.False(view.IsFavorite);
        Assert.Empty(view.Labels!);
    }

    [Fact]
    public void List_NeverIncludesNotesAccountNumberPinOrCustomFieldValues()
    {
        // Trust: the same "no secrets in the list view" rule that already
        // applies to Password must extend to every new secret field.
        var service = CreateService();
        service.Add(
            VaultCredential, "Bank", "firas", "hunter2", null,
            notes: "super secret note", accountNumber: "ACC-99999", pin: "0000",
            customFields: new[] { ("Recovery Code", "leaked-recovery-code", CustomFieldType.Secret) });

        var listed = service.List(VaultCredential).Single();

        Assert.Null(listed.Password);
        Assert.Null(listed.Notes);
        Assert.Null(listed.AccountNumber);
        Assert.Null(listed.Pin);
        Assert.True(listed.CustomFields is null || listed.CustomFields.Count == 0);
    }

    [Fact]
    public void List_IncludesPhoneFavoriteAndLabels_SinceTheyAreNotSecrets()
    {
        var service = CreateService();
        service.Add(VaultCredential, "Bank", "firas", "hunter2", null,
            phone: "555-0100", isFavorite: true, labels: new[] { "Finance" });

        var listed = service.List(VaultCredential).Single();

        Assert.Equal("555-0100", listed.Phone);
        Assert.True(listed.IsFavorite);
        Assert.Equal(new[] { "Finance" }, listed.Labels);
    }

    [Fact]
    public void RawStorage_NeverContainsExtendedSecretFieldsAsPlaintext()
    {
        var service = CreateService();
        service.Add(
            VaultCredential, "Bank", "firas", "hunter2", null,
            notes: "super-secret-note-value", accountNumber: "ACC-77777", pin: "9999",
            customFields: new[] { ("Field", "custom-secret-value", CustomFieldType.Text) });

        var raw = File.ReadAllText(Path.Combine(_dataDirectory, "credentials.json"));

        Assert.DoesNotContain("super-secret-note-value", raw);
        Assert.DoesNotContain("ACC-77777", raw);
        Assert.DoesNotContain("9999", raw);
        Assert.DoesNotContain("custom-secret-value", raw);
    }

    [Fact]
    public void Edit_UpdatesExtendedFields()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "Bank", "firas", "hunter2", null, phone: "555-0100");

        service.Edit(VaultCredential, id, "Bank", "firas", "hunter2", null, phone: "555-9999", isFavorite: true);

        var view = service.GetById(VaultCredential, id);
        Assert.Equal("555-9999", view.Phone);
        Assert.True(view.IsFavorite);
    }

    [Fact]
    public void SetFavorite_TogglesWithoutRequiringEveryOtherField()
    {
        var service = CreateService();
        var id = service.Add(VaultCredential, "Bank", "firas", "hunter2", null);

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
    public void Search_MatchesByPhoneOrLabel()
    {
        var service = CreateService();
        service.Add(VaultCredential, "Bank", "firas", "hunter2", null, phone: "555-0100", labels: new[] { "Finance" });
        service.Add(VaultCredential, "Other", "user2", "pw", null);

        Assert.Single(service.Search(VaultCredential, "555-0100"));
        Assert.Single(service.Search(VaultCredential, "Finance"));
    }
}
