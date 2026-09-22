using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;
using Sifra.Vault.Sync;
using static Sifra.Vault.Tests.CredentialFieldTestHelpers;

namespace Sifra.Vault.Tests;

public sealed class VaultSyncServiceTests : IDisposable
{
    private readonly string _deviceADirectory;
    private readonly string _deviceBDirectory;
    private readonly LocalFakeVaultEnvelopeCloudStore _cloud = new();

    public VaultSyncServiceTests()
    {
        _deviceADirectory = Path.Combine(Path.GetTempPath(), "sifra-sync-a-" + Guid.NewGuid());
        _deviceBDirectory = Path.Combine(Path.GetTempPath(), "sifra-sync-b-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_deviceADirectory)) Directory.Delete(_deviceADirectory, recursive: true);
        if (Directory.Exists(_deviceBDirectory)) Directory.Delete(_deviceBDirectory, recursive: true);
    }

    private VaultSyncService DeviceA(AuditLogger? auditLogger = null) =>
        new(new CredentialStore(_deviceADirectory), new CredentialTombstoneStore(_deviceADirectory), new Sifra.Vault.Tags.TagStore(_deviceADirectory),
            new Sifra.Vault.Attachments.AttachmentStore(_deviceADirectory), new AttachmentTombstoneStore(_deviceADirectory), _cloud, auditLogger);

    private VaultSyncService DeviceB(AuditLogger? auditLogger = null) =>
        new(new CredentialStore(_deviceBDirectory), new CredentialTombstoneStore(_deviceBDirectory), new Sifra.Vault.Tags.TagStore(_deviceBDirectory),
            new Sifra.Vault.Attachments.AttachmentStore(_deviceBDirectory), new AttachmentTombstoneStore(_deviceBDirectory), _cloud, auditLogger);

    /// <summary>Wires Device A and Device B to share the same vault master key, via the Phase 3 shared-vault-key mechanism, so Device B can actually decrypt what it pulls.</summary>
    private void JoinDeviceBToDeviceAsVault(string masterPassword)
    {
        var sharedCloudKeySlots = new Sifra.Vault.Crypto.LocalFakeCloudKeySlotStore();
        var vaultServiceA = new Sifra.Vault.VaultService(new Sifra.Vault.VaultStore(_deviceADirectory));
        var authenticatorA = new Sifra.Vault.Auth.VaultAuthenticator(
            new Sifra.Vault.Auth.VaultAccessCredentialStore(_deviceADirectory),
            new Sifra.Vault.Auth.FileAccessAuditLog(Path.Combine(_deviceADirectory, "vault-access.log")));
        var encryptionA = new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceADirectory));
        var recoveryKeyA = vaultServiceA.CreateVault();
        new Sifra.Vault.Session.VaultRecoveryService(vaultServiceA, authenticatorA, encryptionA).EstablishRecoverySlot(recoveryKeyA, masterPassword);
        new Sifra.Vault.Crypto.VaultDeviceEnrollmentService(
                new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceADirectory), encryptionA, authenticatorA, sharedCloudKeySlots)
            .PublishMasterPasswordSlot();

        var authenticatorB = new Sifra.Vault.Auth.VaultAuthenticator(
            new Sifra.Vault.Auth.VaultAccessCredentialStore(_deviceBDirectory),
            new Sifra.Vault.Auth.FileAccessAuditLog(Path.Combine(_deviceBDirectory, "vault-access.log")));
        var encryptionB = new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceBDirectory));
        new Sifra.Vault.Crypto.VaultDeviceEnrollmentService(
                new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceBDirectory), encryptionB, authenticatorB, sharedCloudKeySlots)
            .JoinExistingVault(masterPassword);
    }

    private static string AddCredential(string dataDirectory, string label = "GitHub") =>
        new Sifra.Vault.Credentials.CredentialService(
                new CredentialStore(dataDirectory), new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(dataDirectory)),
                new FakeCredentialClipboard())
            .Add("correct-horse-battery-staple", label, LoginFields("firas", "hunter2", "https://github.com"));

    [Fact]
    public void SyncNow_FirstEverSync_PublishesLocalCredentialsToTheCloud()
    {
        AddCredential(_deviceADirectory);

        DeviceA().SyncNow();

        var pulled = _cloud.Pull();
        Assert.NotNull(pulled);
        Assert.Single(pulled!.Value.Envelope.Credentials);
    }

    [Fact]
    public void SyncNow_SecondDevice_PullsCredentialsPublishedByTheFirst()
    {
        AddCredential(_deviceADirectory, "GitHub");
        DeviceA().SyncNow();

        DeviceB().SyncNow();

        var deviceBCredentials = new CredentialStore(_deviceBDirectory).GetAll();
        Assert.Single(deviceBCredentials);
        Assert.Equal("GitHub", deviceBCredentials[0].Label);
    }

    [Fact]
    public void SyncNow_BothDevicesEditDifferentCredentials_BothSurviveOnBothDevicesAfterSyncing()
    {
        var idA = AddCredential(_deviceADirectory, "GitHub");
        DeviceA().SyncNow();
        DeviceB().SyncNow(); // Device B now has GitHub too

        var idB = AddCredential(_deviceBDirectory, "Gmail");
        DeviceB().SyncNow();
        DeviceA().SyncNow();

        var onA = new CredentialStore(_deviceADirectory).GetAll();
        var onB = new CredentialStore(_deviceBDirectory).GetAll();
        Assert.Equal(2, onA.Count);
        Assert.Equal(2, onB.Count);
        Assert.Contains(onA, c => c.Id == idA);
        Assert.Contains(onA, c => c.Id == idB);
    }

    [Fact]
    public void SyncNow_BothDevicesEditDifferentFieldsOfTheSameCredential_MergesBothFieldsAfterBothSync()
    {
        // The Monday-username / Tuesday-password scenario from the design
        // discussion, now exercised end-to-end through real sync. Editing a
        // pulled-in credential requires decrypting it, so — matching real
        // usage — Device B must first join Device A's vault via the
        // shared-vault-key mechanism (same VMK), not just receive its
        // ciphertext with no way to read it.
        const string vaultCredential = "correct-horse-battery-staple";
        var encryptionA = new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceADirectory));
        var credentialsA = new CredentialService(new CredentialStore(_deviceADirectory), encryptionA, new FakeCredentialClipboard());
        var id = credentialsA.Add(vaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));

        JoinDeviceBToDeviceAsVault(vaultCredential);

        DeviceA().SyncNow();
        DeviceB().SyncNow();

        // Device A edits the username field only.
        credentialsA.Edit(vaultCredential, id, "GitHub", [("Username", "firas-new", CustomFieldType.Login), ("Password", "hunter2", CustomFieldType.Password), ("Website", "https://github.com", CustomFieldType.Website)]);
        DeviceA().SyncNow();

        // Device B, still on the old pull, edits the password field only.
        var encryptionB = new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceBDirectory));
        var credentialsB = new CredentialService(new CredentialStore(_deviceBDirectory), encryptionB, new FakeCredentialClipboard());
        credentialsB.Edit(vaultCredential, id, "GitHub", [("Username", "firas", CustomFieldType.Login), ("Password", "hunter3", CustomFieldType.Password), ("Website", "https://github.com", CustomFieldType.Website)]);
        DeviceB().SyncNow(); // pulls Device A's username edit, merges, pushes both

        DeviceA().SyncNow(); // catches up on Device B's password edit

        var finalOnA = credentialsA.GetById(vaultCredential, id);
        Assert.Equal("firas-new", finalOnA.Username());
        Assert.Equal("hunter3", finalOnA.Password());
    }

    [Fact]
    public void SyncNow_CredentialDeletedOnOneDeviceAfterTheOtherPulledItStale_StaysDeletedAfterBothSync()
    {
        var encryptionA = new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceADirectory));
        var credentialsA = new CredentialService(
            new CredentialStore(_deviceADirectory), encryptionA, new FakeCredentialClipboard(),
            tombstones: new CredentialTombstoneStore(_deviceADirectory));
        var id = credentialsA.Add("correct-horse-battery-staple", "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        DeviceA().SyncNow();
        DeviceB().SyncNow(); // Device B now has a stale local copy

        credentialsA.Delete(id);
        DeviceA().SyncNow();

        // Device B never touched it, just re-syncs.
        DeviceB().SyncNow();

        Assert.Empty(new CredentialStore(_deviceBDirectory).GetAll());
    }

    [Fact]
    public void SyncNow_WhenAnotherDeviceWinsThePushRaceOnce_RetriesAndStillSucceeds()
    {
        // The exact race scenario from the design discussion: this
        // device's pull and push straddle another device's successful
        // push. The conditional-write rejection must trigger a re-pull
        // and retry, not data loss or an unhandled failure.
        AddCredential(_deviceADirectory, "GitHub");
        DeviceA().SyncNow();

        var idB = AddCredential(_deviceBDirectory, "Gmail-added-by-device-b-directly-to-cloud");
        _cloud.ConcurrentWriteDuringNextPull = () =>
            _cloud.ForceRemoteWrite(new VaultSyncEnvelope(new CredentialStore(_deviceBDirectory).GetAll(), [], [], [], []));

        var deviceA = DeviceA();
        deviceA.SyncNow(); // first pull triggers the concurrent write; push must be rejected once, then retried

        Assert.True(_cloud.PushAttempts >= 2);
        var finalCredentials = new CredentialStore(_deviceADirectory).GetAll();
        Assert.Contains(finalCredentials, c => c.Id == idB); // Device B's concurrently-pushed credential wasn't lost
    }

    [Fact]
    public void SyncNow_WithAuditLoggerProvided_LogsSuccessWithoutLeakingCredentialData()
    {
        AddCredential(_deviceADirectory);
        var sink = new FileAuditLogSink(Path.Combine(_deviceADirectory, "operations.log"));
        var logger = new AuditLogger(sink, new LocalFakeAdminAlertSink());

        DeviceA(logger).SyncNow();

        var lines = sink.ReadAll();
        Assert.Contains(lines, l => l.Contains("SyncNow") && l.Contains("outcome=success"));
        Assert.All(lines, l => Assert.DoesNotContain("hunter2", l));
    }

    [Fact]
    public void SyncNow_TombstoneOlderThanRetention_IsPurgedAndStopsBeingPropagated()
    {
        // Simulates a deletion old enough that every device has long since
        // had a chance to sync past it (see VaultSyncService.TombstoneRetention).
        var tombstonesA = new CredentialTombstoneStore(_deviceADirectory);
        tombstonesA.Add("ancient-cred", DateTimeOffset.UtcNow.AddDays(-91));

        DeviceA().SyncNow();

        Assert.Null(tombstonesA.Load("ancient-cred")); // purged from local storage
        var pulled = _cloud.Pull();
        Assert.DoesNotContain(pulled!.Value.Envelope.Tombstones, t => t.Id == "ancient-cred"); // never propagated to the cloud
    }

    [Fact]
    public void SyncNow_TombstoneWithinRetention_IsStillKeptAndPropagated()
    {
        var tombstonesA = new CredentialTombstoneStore(_deviceADirectory);
        tombstonesA.Add("recent-cred", DateTimeOffset.UtcNow.AddDays(-1));

        DeviceA().SyncNow();

        Assert.NotNull(tombstonesA.Load("recent-cred"));
        var pulled = _cloud.Pull();
        Assert.Contains(pulled!.Value.Envelope.Tombstones, t => t.Id == "recent-cred");
    }

    [Fact]
    public void SyncNow_OldTombstonePurgedByOneDevice_DoesNotGetResurrectedWhenAnotherDeviceStillHasItLocally()
    {
        // The bug this design specifically avoids: purging only the LOCAL
        // table (not also dropping it from what gets pushed) would mean
        // the very next pull from a device that hasn't purged yet brings
        // it straight back. Device B here still has its own old local
        // copy — Device A's purge-and-push must not be undone by it.
        var tombstonesA = new CredentialTombstoneStore(_deviceADirectory);
        var oldTimestamp = DateTimeOffset.UtcNow.AddDays(-91);
        tombstonesA.Add("ancient-cred", oldTimestamp);
        new CredentialTombstoneStore(_deviceBDirectory).Add("ancient-cred", oldTimestamp); // Device B independently has the same old tombstone, never yet synced

        DeviceA().SyncNow(); // purges it from A and from what's pushed
        DeviceB().SyncNow(); // B pulls A's envelope (without it) and merges against its own old local copy

        var pulled = _cloud.Pull();
        Assert.DoesNotContain(pulled!.Value.Envelope.Tombstones, t => t.Id == "ancient-cred");
        Assert.Null(new CredentialTombstoneStore(_deviceBDirectory).Load("ancient-cred"));
    }

    [Fact]
    public void SyncNow_TagsAddedOnDifferentDevices_BothSurviveOnBothDevicesAfterSyncing()
    {
        new Sifra.Vault.Tags.TagService(new Sifra.Vault.Tags.TagStore(_deviceADirectory)).Add("Work", "#ff0000", false);
        DeviceA().SyncNow();
        DeviceB().SyncNow();

        new Sifra.Vault.Tags.TagService(new Sifra.Vault.Tags.TagStore(_deviceBDirectory)).Add("Personal", "#00ff00", false);
        DeviceB().SyncNow();
        DeviceA().SyncNow();

        var tagsOnA = new Sifra.Vault.Tags.TagStore(_deviceADirectory).GetAll();
        var tagsOnB = new Sifra.Vault.Tags.TagStore(_deviceBDirectory).GetAll();
        Assert.Equal(2, tagsOnA.Count);
        Assert.Equal(2, tagsOnB.Count);
        Assert.Contains(tagsOnA, t => t.Name == "Work");
        Assert.Contains(tagsOnA, t => t.Name == "Personal");
    }

    [Fact]
    public void SyncNow_AttachmentAddedLocally_PropagatesMetadataButNotBlobBytesToTheOtherDevice()
    {
        var vaultCredential = "correct-horse-battery-staple";
        var credentialId = AddCredential(_deviceADirectory);
        var attachmentsA = new Sifra.Vault.Attachments.CredentialAttachmentService(
            new Sifra.Vault.Attachments.AttachmentStore(_deviceADirectory),
            new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceADirectory)));
        var attachmentId = attachmentsA.Add(vaultCredential, credentialId, "photo.jpg", Sifra.Vault.Attachments.AttachmentKind.Image, [1, 2, 3]);

        DeviceA().SyncNow();
        DeviceB().SyncNow();

        var attachmentStoreB = new Sifra.Vault.Attachments.AttachmentStore(_deviceBDirectory);
        var metadataOnB = attachmentStoreB.FindById(attachmentId);
        Assert.NotNull(metadataOnB);
        Assert.Equal("photo.jpg", metadataOnB!.FileName);
        // Blob bytes are explicitly out of scope for the metadata envelope —
        // they sync separately, per-file, not yet implemented — so reading
        // the blob on the device that never had it must fail cleanly rather
        // than silently succeed with garbage.
        Assert.Throws<VaultStorageException>(() => attachmentStoreB.ReadEncryptedBlob(attachmentId));
    }

    [Fact]
    public void SyncNow_AttachmentDeletedOnOneDeviceAfterTheOtherPulledItStale_StaysDeletedAfterBothSync()
    {
        var vaultCredential = "correct-horse-battery-staple";
        var credentialId = AddCredential(_deviceADirectory);
        var attachmentStoreA = new Sifra.Vault.Attachments.AttachmentStore(_deviceADirectory);
        var attachmentsA = new Sifra.Vault.Attachments.CredentialAttachmentService(
            attachmentStoreA, new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceADirectory)),
            tombstones: new AttachmentTombstoneStore(_deviceADirectory));
        var attachmentId = attachmentsA.Add(vaultCredential, credentialId, "photo.jpg", Sifra.Vault.Attachments.AttachmentKind.Image, [1, 2, 3]);
        DeviceA().SyncNow();
        DeviceB().SyncNow();

        attachmentsA.Delete(attachmentId);
        DeviceA().SyncNow();
        DeviceB().SyncNow();

        Assert.Null(new Sifra.Vault.Attachments.AttachmentStore(_deviceBDirectory).FindById(attachmentId));
    }

    [Fact]
    public void SyncNow_CredentialDeleted_AlsoDeletesItsAttachmentsEverywhere()
    {
        var vaultCredential = "correct-horse-battery-staple";
        var credentialsA = new CredentialService(
            new CredentialStore(_deviceADirectory), new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceADirectory)),
            new FakeCredentialClipboard(), tombstones: new CredentialTombstoneStore(_deviceADirectory));
        var credentialId = credentialsA.Add(vaultCredential, "GitHub", LoginFields("firas", "hunter2", "https://github.com"));
        var attachmentsA = new Sifra.Vault.Attachments.CredentialAttachmentService(
            new Sifra.Vault.Attachments.AttachmentStore(_deviceADirectory), new Sifra.Vault.Crypto.VaultEncryptionService(new Sifra.Vault.Crypto.VaultMasterKeyStore(_deviceADirectory)));
        var attachmentId = attachmentsA.Add(vaultCredential, credentialId, "photo.jpg", Sifra.Vault.Attachments.AttachmentKind.Image, [1, 2, 3]);
        DeviceA().SyncNow();
        DeviceB().SyncNow();
        Assert.NotNull(new Sifra.Vault.Attachments.AttachmentStore(_deviceBDirectory).FindById(attachmentId));

        credentialsA.Delete(credentialId);
        DeviceA().SyncNow();
        DeviceB().SyncNow();

        Assert.Null(new Sifra.Vault.Attachments.AttachmentStore(_deviceADirectory).FindById(attachmentId));
        Assert.Null(new Sifra.Vault.Attachments.AttachmentStore(_deviceBDirectory).FindById(attachmentId));
    }

    [Fact]
    public void SyncNow_WhenTheCloudIsDisconnected_ThrowsSyncUnavailable()
    {
        AddCredential(_deviceADirectory);
        _cloud.IsConnected = false;

        Assert.Throws<SyncUnavailableException>(() => DeviceA().SyncNow());
    }
}
