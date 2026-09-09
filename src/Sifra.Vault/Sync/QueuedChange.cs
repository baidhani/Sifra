namespace Sifra.Vault.Sync;

/// <summary>
/// One offline edit waiting to reach the cloud. <see cref="Conflicted"/>
/// distinguishes "still pending, never tried" from "tried and the remote
/// side reported a conflict" — a conflicted change stays in the queue
/// instead of being silently dropped or overwritten.
/// </summary>
public sealed record QueuedChange(
    string ChangeId,
    string ItemId,
    string PayloadJson,
    DateTimeOffset QueuedAtUtc,
    bool Conflicted);
