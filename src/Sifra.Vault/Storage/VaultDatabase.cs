using Microsoft.Data.Sqlite;

namespace Sifra.Vault.Storage;

/// <summary>
/// Shared entry point for every store's SQLite connection. All stores
/// share one file (vault.db) in the same data directory the JSON stores
/// used to live in — SQLite is built to hold many tables in one file, so
/// there is no reason to split per-store the way one-JSON-file-per-store
/// did. Each store still resolves its own data directory the same way
/// (dataDirectory parameter, defaulting to %AppData%\Sifra) and owns its
/// own table(s); this class only owns opening the connection and the
/// directory-creation failure path every store used to duplicate.
/// </summary>
internal static class VaultDatabase
{
    private const string FileName = "vault.db";

    public static string ResolveDataDirectory(string? dataDirectory)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sifra");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException($"Could not create vault storage directory '{directory}'.", ex);
        }

        return directory;
    }

    /// <exception cref="VaultStorageException">The database file could not be opened.</exception>
    public static SqliteConnection OpenConnection(string? dataDirectory)
    {
        var directory = ResolveDataDirectory(dataDirectory);
        var dbPath = Path.Combine(directory, FileName);

        try
        {
            // Pooling=False: this app opens a connection per store call on
            // a local single-user file, not per web request — the pooling
            // benefit doesn't apply, and a pooled connection keeps the
            // file handle open past Dispose(), which breaks tests (and any
            // real caller) that need to delete/move the file right after.
            var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            connection.Open();

            using var pragma = connection.CreateCommand();
            // WAL: readers never block writers and vice versa — this app
            // reads/writes from a single process, but WPF UI code and
            // background tasks (idle-lock timer, pairing server) can touch
            // stores from different threads at once.
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();

            return connection;
        }
        catch (SqliteException ex)
        {
            throw new VaultStorageException($"Could not open vault database '{dbPath}'.", ex);
        }
    }
}
