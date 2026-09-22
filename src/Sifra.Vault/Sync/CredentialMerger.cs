using Sifra.Vault.Credentials;

namespace Sifra.Vault.Sync;

/// <summary>
/// Phase 3 sync merge logic: combines a local and a remote copy of the
/// SAME credential into one, field by field, using each
/// <see cref="CustomField.UpdatedAtUtc"/> as the last-write-wins signal —
/// per the approved design, this is derived entirely from timestamps the
/// vault already maintains (CredentialService.EncryptFields only bumps a
/// field's UpdatedAtUtc when its plaintext actually changed); no separate
/// per-field change log is needed.
///
/// Everything outside Fields (Label, IsFavorite, Tags, Icon) has no
/// finer-grained timestamp than the credential's own UpdatedAtUtc, so it
/// is resolved as a simple whole-record last-write-wins: whichever side's
/// UpdatedAtUtc is newer supplies all of it. This never touches ciphertext
/// or requires the vault master key — merging is pure metadata comparison.
/// </summary>
public static class CredentialMerger
{
    /// <exception cref="ArgumentException">local and remote have different Ids.</exception>
    public static Credential Merge(Credential local, Credential remote)
    {
        if (local.Id != remote.Id)
        {
            throw new ArgumentException($"Cannot merge different credentials (local id '{local.Id}' vs remote id '{remote.Id}').");
        }

        var newer = remote.UpdatedAtUtc > local.UpdatedAtUtc ? remote : local;

        return newer with
        {
            Fields = MergeFields(local, remote),
            CreatedAtUtc = local.CreatedAtUtc < remote.CreatedAtUtc ? local.CreatedAtUtc : remote.CreatedAtUtc,
        };
    }

    private static List<CustomField> MergeFields(Credential local, Credential remote)
    {
        var result = new List<CustomField>();
        var seen = new HashSet<(string Name, CustomFieldType Type)>();

        foreach (var localField in local.Fields)
        {
            var key = (localField.Name, localField.Type);
            seen.Add(key);
            var remoteField = remote.Fields.FirstOrDefault(f => f.Name == key.Name && f.Type == key.Type);

            if (remoteField is null)
            {
                // Present locally, absent remotely: keep it only if it's
                // newer than the remote's last full rewrite of its field
                // list — otherwise the remote side's own later edit is what
                // deliberately dropped this field, and that deletion wins.
                if (localField.UpdatedAtUtc > remote.UpdatedAtUtc)
                {
                    result.Add(localField);
                }
                continue;
            }

            result.Add(remoteField.UpdatedAtUtc > localField.UpdatedAtUtc ? remoteField : localField);
        }

        foreach (var remoteField in remote.Fields)
        {
            var key = (remoteField.Name, remoteField.Type);
            if (!seen.Add(key))
            {
                continue;
            }

            // Present remotely, absent locally: symmetric to the case above.
            if (remoteField.UpdatedAtUtc > local.UpdatedAtUtc)
            {
                result.Add(remoteField);
            }
        }

        return result;
    }
}
