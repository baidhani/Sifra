namespace Sifra.Vault.Sync;

/// <summary>
/// A placeholder unit of vault data — deliberately generic, NOT the real
/// credential model. STORY-002 introduces the real Credential type; this
/// story only needs something concrete to view/edit/queue/sync so the
/// offline-first mechanism is genuinely exercised rather than asserted.
/// The queue and sync machinery below do not know or care what shape the
/// payload is, so swapping this for a real Credential later is a
/// same-shape replacement, not a rewrite.
/// </summary>
public sealed record VaultDataItem(
    string Id,
    string Content,
    DateTimeOffset UpdatedAtUtc);
