using Sifra.Vault.Credentials;
using Sifra.Vault.Crypto;
using static Sifra.Vault.Tests.CredentialFieldTestHelpers;

namespace Sifra.Vault.Tests;

public sealed class PasswordHistoryServiceTests : IDisposable
{
    private const string VaultCredential = "correct-horse-battery-staple";
    private const string CredentialId = "cred-1";

    private readonly string _dataDirectory;

    public PasswordHistoryServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "sifra-password-history-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private PasswordHistoryService CreateHistoryService() =>
        new(new PasswordHistoryStore(_dataDirectory), new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)));

    private CredentialService CreateCredentialService(PasswordHistoryService historyService) => new(
        new CredentialStore(_dataDirectory),
        new VaultEncryptionService(new VaultMasterKeyStore(_dataDirectory)),
        new FakeCredentialClipboard(),
        passwordHistory: historyService);

    [Fact]
    public void Edit_WithAChangedPassword_RecordsTheOldValueInHistory()
    {
        var history = CreateHistoryService();
        var credentials = CreateCredentialService(history);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "OldPassword1!", null));

        credentials.Edit(VaultCredential, id, "Bank", LoginFields("user", "NewPassword2!", null));

        var entries = history.List(VaultCredential, id, "Password");
        var entry = Assert.Single(entries);
        Assert.Equal("OldPassword1!", entry.Value);
    }

    [Fact]
    public void Edit_WithoutChangingThePassword_RecordsNothing()
    {
        // A no-op edit (e.g. only the label changes) must not spam history.
        var history = CreateHistoryService();
        var credentials = CreateCredentialService(history);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "SamePassword1!", null));

        credentials.Edit(VaultCredential, id, "Bank Renamed", LoginFields("user", "SamePassword1!", null));

        Assert.Empty(history.List(VaultCredential, id, "Password"));
    }

    [Fact]
    public void Add_BeyondTwentyEntries_KeepsOnlyTheNewestTwenty()
    {
        var history = CreateHistoryService();
        var credentials = CreateCredentialService(history);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "Password0", null));

        for (var i = 1; i <= 25; i++)
        {
            credentials.Edit(VaultCredential, id, "Bank", LoginFields("user", $"Password{i}", null));
        }

        var entries = history.List(VaultCredential, id, "Password");
        Assert.Equal(20, entries.Count);
        // Newest-first: the most recently replaced value (Password24, right
        // before the final Password25 edit) should be present; the earliest
        // ones (Password0-4) should have been dropped.
        Assert.Contains(entries, e => e.Value == "Password24");
        Assert.DoesNotContain(entries, e => e.Value == "Password0");
    }

    [Fact]
    public void Clear_RemovesHistoryForThatFieldOnly()
    {
        var history = CreateHistoryService();
        var credentials = CreateCredentialService(history);
        var id = credentials.Add(VaultCredential, "Bank", LoginFields("user", "Old1", null));
        credentials.Edit(VaultCredential, id, "Bank", LoginFields("user", "New1", null));

        history.Clear(id, "Password");

        Assert.Empty(history.List(VaultCredential, id, "Password"));
    }

    [Fact]
    public void DeleteAllForCredential_RemovesOnlyThatCredentialsHistory()
    {
        var history = CreateHistoryService();
        var credentials = CreateCredentialService(history);
        var id1 = credentials.Add(VaultCredential, "Bank", LoginFields("user", "Old1", null));
        credentials.Edit(VaultCredential, id1, "Bank", LoginFields("user", "New1", null));
        var id2 = credentials.Add(VaultCredential, "Other", LoginFields("user", "Old2", null));
        credentials.Edit(VaultCredential, id2, "Other", LoginFields("user", "New2", null));

        history.DeleteAllForCredential(id1);

        Assert.Empty(history.List(VaultCredential, id1, "Password"));
        Assert.Single(history.List(VaultCredential, id2, "Password"));
    }
}
