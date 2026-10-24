using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Syphon.Engine;

/// <summary>One owner per database. Task metadata and its block manifest commit atomically.</summary>
internal sealed class DownloadStore : IDisposable
{
    static DownloadStore() => SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
    private readonly FileStream ownership;
    private readonly SqliteConnection connection;

    public DownloadStore(string directory)
    {
        Directory.CreateDirectory(directory);
        ownership = new FileStream(Path.Combine(directory, "session.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(directory, "tasks.db"), Pooling = false }.ToString());
        try
        {
            connection.Open();
            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA quick_check";
            if (!string.Equals(integrity.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                throw new InvalidDataException("Syphon's saved history database is damaged. The original files have been left in place. Preserve tasks.db and its journal files before attempting recovery.");
            using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            var schema = Convert.ToInt32(version.ExecuteScalar());
            if (schema > 9) throw new InvalidDataException("This download database needs a newer Syphon version.");
            using var setup = connection.CreateCommand();
            setup.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
            setup.ExecuteNonQuery();
            using var migration = connection.BeginTransaction();
            using var create = connection.CreateCommand();
            create.Transaction = migration;
            create.CommandText = "CREATE TABLE IF NOT EXISTS tasks (position INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT UNIQUE NOT NULL, snapshot TEXT NOT NULL); CREATE TABLE IF NOT EXISTS browser_receipts (request_id TEXT PRIMARY KEY, url_hash TEXT NOT NULL, task_id TEXT NOT NULL); PRAGMA user_version=9;";
            create.ExecuteNonQuery();
            migration.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26)
        {
            connection.Dispose(); ownership.Dispose();
            throw new InvalidDataException("Syphon's saved history database is damaged. The original files have been left in place. Preserve tasks.db and its journal files before attempting recovery.", ex);
        }
        catch { connection.Dispose(); ownership.Dispose(); throw; }
    }

    public StoredDownload[] Load()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot FROM tasks ORDER BY position";
        using var reader = command.ExecuteReader();
        var tasks = new List<StoredDownload>();
        while (reader.Read()) tasks.Add(JsonSerializer.Deserialize<StoredDownload>(reader.GetString(0))
            ?? throw new InvalidDataException("A saved task could not be read."));
        return tasks.ToArray();
    }

    public void Save(StoredDownload task)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO tasks(id,snapshot) VALUES($id,$snapshot) ON CONFLICT(id) DO UPDATE SET snapshot=excluded.snapshot";
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(task with { Received = task.Committed, BytesPerSecond = 0 }));
        command.ExecuteNonQuery();
        if (task.BrowserRequestId is Guid requestId)
        {
            using var receipt = connection.CreateCommand();
            receipt.Transaction = transaction;
            receipt.CommandText = "INSERT INTO browser_receipts(request_id,url_hash,task_id) VALUES($id,$hash,$task) ON CONFLICT(request_id) DO NOTHING";
            receipt.Parameters.AddWithValue("$id", requestId.ToString());
            receipt.Parameters.AddWithValue("$hash", BrowserIdentityHash(task.Url, task.MediaFormat, task.VideoStream, task.AudioStream, task.YoutubeFormat, task.QualityHeight));
            receipt.Parameters.AddWithValue("$task", task.Id.ToString());
            receipt.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void Dispose() { connection.Dispose(); ownership.Dispose(); }

    public void SaveMany(IEnumerable<StoredDownload> tasks)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var task in tasks)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE tasks SET snapshot=$snapshot WHERE id=$id";
            command.Parameters.AddWithValue("$id", task.Id.ToString());
            command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(task with { Received = task.Committed, BytesPerSecond = 0 }));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void RemoveHistory(Guid[] ids)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var id in ids) {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "DELETE FROM tasks WHERE id=$id"; command.Parameters.AddWithValue("$id", id.ToString()); command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void Remove(Guid id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tasks WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    internal static string BrowserUrlHash(string url) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
    internal static string BrowserIdentityHash(string url, string? format, int? video, int? audio, string? youtubeFormat = null, int? qualityHeight = null) =>
        BrowserUrlHash((format is null ? url : $"{url}\nmedia:{format}:{video}:{audio}") + (youtubeFormat is null ? "" : "\nyoutube:" + youtubeFormat) + (qualityHeight is null ? "" : "\nheight:" + qualityHeight));
    internal void RegisterExistingReceipt(Guid requestId, Guid taskId, string hash)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO browser_receipts(request_id,url_hash,task_id) VALUES($id,$hash,$task) ON CONFLICT(request_id) DO NOTHING";
        command.Parameters.AddWithValue("$id", requestId.ToString());command.Parameters.AddWithValue("$hash", hash);command.Parameters.AddWithValue("$task", taskId.ToString());command.ExecuteNonQuery();
    }
    public (Guid TaskId, string UrlHash)? FindBrowserRequest(Guid requestId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id,url_hash FROM browser_receipts WHERE request_id=$id";
        command.Parameters.AddWithValue("$id", requestId.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? (Guid.Parse(reader.GetString(0)), reader.GetString(1)) : null;
    }
}
