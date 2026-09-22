using Microsoft.Data.Sqlite;
using Sifra.Vault.Storage;

namespace Sifra.Vault;

/// <summary>
/// Persists this device's local copy of the vault's identity fingerprint
/// (see VaultEncryptionService.ComputeVaultId) — a non-secret, one-way
/// hash of the vault master key, computed independently on every device
/// that ever unwraps the SAME VMK (via EstablishRecoverySlot, Recover, or
/// VaultDeviceEnrollmentService.JoinExistingVault), so devices that
/// legitimately share a vault always end up with the identical value with
/// no synchronization needed. VaultSyncService compares this against a
/// pulled envelope's own VaultId to refuse merging in a genuinely
/// different vault's data (see VaultMismatchException).
/// </summary>
public sealed class VaultIdentityStore
{
    private readonly string? _dataDirectory;

    public VaultIdentityStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory;

        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        EnsureSchema(connection);
    }

    public string? LoadVaultId()
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT vault_id FROM vault_identity WHERE id = 1";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Idempotent — safe to call every time the VMK is unwrapped, not just the first time.</summary>
    public void SaveVaultId(string vaultId)
    {
        using var connection = VaultDatabase.OpenConnection(_dataDirectory);
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO vault_identity (id, vault_id) VALUES (1, $vaultId) ON CONFLICT(id) DO UPDATE SET vault_id = excluded.vault_id";
            cmd.Parameters.AddWithValue("$vaultId", vaultId);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException("Could not write to the vault identity database.", ex);
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS vault_identity (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                vault_id TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
