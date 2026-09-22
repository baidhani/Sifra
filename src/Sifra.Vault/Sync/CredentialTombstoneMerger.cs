using Sifra.Vault.Credentials;

namespace Sifra.Vault.Sync;

public enum CredentialMergeOutcome
{
    /// <summary>The credential survives the merge — see <see cref="CredentialMergeResult.Credential"/>.</summary>
    Present,

    /// <summary>The credential is deleted — see <see cref="CredentialMergeResult.DeletedAtUtc"/>.</summary>
    Deleted,
}

public sealed record CredentialMergeResult(CredentialMergeOutcome Outcome, Credential? Credential, DateTimeOffset? DeletedAtUtc);

/// <summary>
/// Extends <see cref="CredentialMerger"/> with deletion tombstones: for one
/// credential id, reconciles "local view" (either a live Credential or a
/// tombstone) against "remote view" (same choice) into one outcome.
///
/// A tombstone competes against a live record using the SAME last-write-wins
/// rule as everything else in this sync design — the live record's own
/// Credential.UpdatedAtUtc (bumped on every kind of edit: field changes,
/// tag/favorite/icon changes) is compared against the tombstone's
/// DeletedAtUtc. Newer wins: an edit after the deletion resurrects the
/// credential (the user's intent to keep it is the more recent signal);
/// a deletion after the last known edit keeps it gone.
/// </summary>
public static class CredentialTombstoneMerger
{
    /// <exception cref="ArgumentException">
    /// Neither side supplies a Credential nor a DeletedAtUtc (nothing to merge), or
    /// the two Credentials present have different Ids.
    /// </exception>
    public static CredentialMergeResult Merge(
        Credential? localCredential, DateTimeOffset? localDeletedAtUtc,
        Credential? remoteCredential, DateTimeOffset? remoteDeletedAtUtc)
    {
        if (localCredential is null && localDeletedAtUtc is null)
        {
            throw new ArgumentException("Local side must supply either a Credential or a DeletedAtUtc.");
        }

        if (remoteCredential is null && remoteDeletedAtUtc is null)
        {
            throw new ArgumentException("Remote side must supply either a Credential or a DeletedAtUtc.");
        }

        // Both alive: defer to the plain field-level merge, no deletion involved.
        if (localDeletedAtUtc is null && remoteDeletedAtUtc is null)
        {
            return new CredentialMergeResult(
                CredentialMergeOutcome.Present, CredentialMerger.Merge(localCredential!, remoteCredential!), null);
        }

        // Both deleted: stays deleted — keep the later timestamp (nearer to
        // the truth of "when every device had agreed it was gone").
        if (localDeletedAtUtc is not null && remoteDeletedAtUtc is not null)
        {
            var newest = localDeletedAtUtc.Value > remoteDeletedAtUtc.Value ? localDeletedAtUtc.Value : remoteDeletedAtUtc.Value;
            return new CredentialMergeResult(CredentialMergeOutcome.Deleted, null, newest);
        }

        // Exactly one side deleted: the alive side's last-touched timestamp
        // races the tombstone's timestamp.
        var (deletedAtUtc, aliveCredential) = localDeletedAtUtc is not null
            ? (localDeletedAtUtc.Value, remoteCredential!)
            : (remoteDeletedAtUtc!.Value, localCredential!);

        return aliveCredential.UpdatedAtUtc > deletedAtUtc
            ? new CredentialMergeResult(CredentialMergeOutcome.Present, aliveCredential, null)
            : new CredentialMergeResult(CredentialMergeOutcome.Deleted, null, deletedAtUtc);
    }
}
