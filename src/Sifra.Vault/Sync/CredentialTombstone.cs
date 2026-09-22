namespace Sifra.Vault.Sync;

/// <summary>
/// Marks a credential as deleted, with a timestamp — written instead of
/// just removing the row outright, so a sync merge can tell "genuinely
/// deleted" apart from "a stale device just hasn't synced this credential
/// yet." Without this, a device that was offline when a credential was
/// deleted elsewhere would push its still-locally-present copy back up
/// during its next sync and silently resurrect it.
/// </summary>
public sealed record CredentialTombstone(string Id, DateTimeOffset DeletedAtUtc);
