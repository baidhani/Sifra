namespace Sifra.Vault.Sync;

/// <summary>
/// Thrown when a push kept losing the revision race — someone else keeps
/// winning the conditional write before this device's retry lands. Per
/// this project's ban on unbounded retry loops, VaultSyncService gives up
/// after a capped number of attempts rather than retrying forever; local
/// data is untouched (the merge already applied locally regardless — only
/// the push to the cloud didn't land), so the next manual or scheduled
/// sync simply tries again from scratch.
/// </summary>
public sealed class SyncConflictExhaustedException : Exception
{
    public SyncConflictExhaustedException(int attempts)
        : base($"Could not push to the cloud after {attempts} attempts — another device kept winning the write race.")
    {
    }
}
