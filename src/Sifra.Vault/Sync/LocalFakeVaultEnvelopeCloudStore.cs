namespace Sifra.Vault.Sync;

/// <summary>
/// Stand-in cloud envelope store with no network calls — this repo has no
/// real provider wired to the vault sync envelope yet. Models revisioning
/// with a simple incrementing counter (real providers use their own
/// opaque rev/ETag, but VaultSyncService only ever compares revisions for
/// equality, so this is behaviorally equivalent for tests). Two
/// VaultSyncService instances pointed at the same instance of this class
/// simulate two devices sharing one cloud account.
/// </summary>
public sealed class LocalFakeVaultEnvelopeCloudStore : IVaultEnvelopeCloudStore
{
    private VaultSyncEnvelope? _envelope;
    private int _revision;

    public bool IsConnected { get; set; } = true;

    public int PushAttempts { get; private set; }

    /// <summary>
    /// Fires exactly once, the next time Pull() is called, then clears
    /// itself — lets a test simulate "another device's push landed in the
    /// instant between my pull and my push," which is what a capped-retry
    /// push loop needs to prove it actually recovers from.
    /// </summary>
    public Action? ConcurrentWriteDuringNextPull { get; set; }

    /// <summary>Directly mutates the remote, bypassing revision checks — represents another device's already-succeeded push, for test setup.</summary>
    public void ForceRemoteWrite(VaultSyncEnvelope envelope)
    {
        _envelope = envelope;
        _revision++;
    }

    public (VaultSyncEnvelope Envelope, string Revision)? Pull()
    {
        EnsureConnected();

        var result = _envelope is null ? default((VaultSyncEnvelope, string)?) : (_envelope, _revision.ToString());

        var hook = ConcurrentWriteDuringNextPull;
        ConcurrentWriteDuringNextPull = null;
        hook?.Invoke();

        return result;
    }

    public bool TryPush(VaultSyncEnvelope envelope, string? expectedRevision, out string newRevision)
    {
        EnsureConnected();
        PushAttempts++;

        var currentRevision = _envelope is null ? null : _revision.ToString();
        if (currentRevision != expectedRevision)
        {
            newRevision = currentRevision ?? "0";
            return false;
        }

        _envelope = envelope;
        _revision++;
        newRevision = _revision.ToString();
        return true;
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new SyncUnavailableException();
        }
    }
}
