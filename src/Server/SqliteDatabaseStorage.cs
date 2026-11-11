// Modified for the macOS headless host, 2026-11-11. GPL-3.0-only.
using System.IO.Enumeration;
using Microsoft.Data.Sqlite;
using Server;

namespace Server.ConsoleHost;

public sealed class SqliteDatabaseStorage : DatabaseStorage, IDisposable
{
    private readonly SqliteConnection connection;
    private readonly object gate = new();
    private SqliteTransaction? transaction;

    public SqliteDatabaseStorage(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS snapshots (
                name TEXT PRIMARY KEY,
                data BLOB NOT NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS previous_snapshots (
                name TEXT PRIMARY KEY,
                data BLOB NOT NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            DROP TABLE IF EXISTS admin_audit;
            """;
        command.ExecuteNonQuery();
    }

    private static string Key(string name) => name.Replace('\\', '/').TrimStart('.', '/');

    public override bool Contains(string name)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT 1 FROM snapshots WHERE name = $name";
            command.Parameters.AddWithValue("$name", Key(name));
            return command.ExecuteScalar() != null;
        }
    }

    public override Stream Read(string name)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT data FROM snapshots WHERE name = $name";
            command.Parameters.AddWithValue("$name", Key(name));
            return new MemoryStream((byte[])(command.ExecuteScalar()
                ?? throw new FileNotFoundException("SQLite snapshot not found", name)), writable: false);
        }
    }

    public override void Write(string name, byte[] data)
    {
        Transaction(() =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR REPLACE INTO previous_snapshots SELECT * FROM snapshots WHERE name = $name;
                INSERT INTO snapshots(name, data, updated_utc) VALUES ($name, $data, $time)
                ON CONFLICT(name) DO UPDATE SET data = excluded.data, updated_utc = excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$name", Key(name));
            command.Parameters.AddWithValue("$data", data);
            command.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        });
    }

    public override void Remove(string name)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM snapshots WHERE name = $name";
            command.Parameters.AddWithValue("$name", Key(name));
            command.ExecuteNonQuery();
        }
    }

    public override string[] List(string directory, string pattern)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT name FROM snapshots ORDER BY name";
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            var prefix = Key(directory).TrimEnd('/') + "/";
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (name.StartsWith(prefix, StringComparison.Ordinal) &&
                    !name[prefix.Length..].Contains('/') &&
                    FileSystemName.MatchesSimpleExpression(pattern, name.AsSpan(prefix.Length)))
                    names.Add(name);
            }
            return names.ToArray();
        }
    }

    public void Import(string source)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key = 'seed_imported'";
        if (command.ExecuteScalar() != null) return;
        // Import only missing snapshots; subsequent runs must never overwrite saved players.
        foreach (var pattern in new[] { "Server.MirDB", "Server.MirADB", "*.mgd", "*.mcd", "*.msd", "*.MirCA" })
        foreach (var file in Directory.EnumerateFiles(source, pattern, SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(source, file);
            if (!Contains(name)) Write(name, File.ReadAllBytes(file));
        }
        command.CommandText = "INSERT INTO metadata VALUES ('seed_imported', $source)";
        command.Parameters.AddWithValue("$source", Path.GetFullPath(source));
        command.ExecuteNonQuery();
    }

    public override void Transaction(Action action)
    {
        lock (gate)
        {
            if (transaction != null) { action(); return; }
            using var pending = connection.BeginTransaction(); transaction = pending;
            try { action(); pending.Commit(); }
            finally { transaction = null; }
        }
    }
    public void Backup(string path)
    {
        lock (gate)
        {
            if (transaction != null) throw new InvalidOperationException("Cannot back up an active transaction.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            target.Open(); connection.BackupDatabase(target);
        }
    }
    public void BackupBeforePlayerUpgrade(string directory)
    {
        if (!Contains(Server.MirEnvir.Envir.AccountPath)) return;
        using var reader = new BinaryReader(Read(Server.MirEnvir.Envir.AccountPath));
        var version = reader.ReadInt32(); var customVersion = reader.ReadInt32();
        if (version > Server.MirEnvir.Envir.Version || customVersion > Server.MirEnvir.Envir.CustomVersion)
            throw new InvalidDataException("Player database was written by a newer engine. Use that engine or restore a complete backup.");
        if (customVersion < Server.MirEnvir.Envir.CustomVersion)
            Backup(Path.Combine(directory, "player-upgrade-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"), "mir2.sqlite3"));
    }
    public void Dispose() => connection.Dispose();
}
