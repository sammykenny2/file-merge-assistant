using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FileMergeAssistant.Index;

/// <summary>
/// The local index (ADR-008): one SQLite file in WAL mode, written by one scan at a time while the interface reads.
/// The schema version lives in <c>PRAGMA user_version</c>; migrations run in order, and an existing index is backed up
/// before the first migration that changes it.
/// </summary>
public sealed class IndexDatabase
{
  /// <summary>Version of the schema this build writes.</summary>
  public const int SchemaVersion = 2;

  private static readonly string[] Migrations =
  [
    // Version 1: sources, scan jobs, files with fingerprints, regenerable and project folders, decisions.
    """
    CREATE TABLE meta (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL
    );
    CREATE TABLE sources (
      id TEXT PRIMARY KEY,
      name TEXT NOT NULL,
      root TEXT NOT NULL,
      is_protected INTEGER NOT NULL,
      skip_regenerable INTEGER NOT NULL,
      skip_cloud INTEGER NOT NULL,
      regenerable_names TEXT NOT NULL,
      exclusions TEXT NOT NULL,
      volume_serial TEXT,
      volume_label TEXT,
      volume_kind TEXT,
      added_at TEXT NOT NULL,
      last_scan_at TEXT,
      last_scan_errors INTEGER NOT NULL DEFAULT 0,
      last_seen_at TEXT,
      scan_number INTEGER NOT NULL DEFAULT 0,
      position INTEGER NOT NULL
    );
    CREATE TABLE files (
      id INTEGER PRIMARY KEY,
      source_id TEXT NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
      rel_path TEXT NOT NULL,
      size INTEGER NOT NULL,
      mtime INTEGER NOT NULL,
      hash TEXT,
      cloud_only INTEGER NOT NULL DEFAULT 0,
      seen INTEGER NOT NULL,
      UNIQUE (source_id, rel_path)
    );
    CREATE INDEX files_size ON files(size) WHERE cloud_only = 0;
    CREATE INDEX files_hash ON files(hash) WHERE hash IS NOT NULL;
    CREATE TABLE regenerable_dirs (
      source_id TEXT NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
      rel_path TEXT NOT NULL,
      name TEXT NOT NULL,
      bytes INTEGER NOT NULL,
      files INTEGER NOT NULL,
      seen INTEGER NOT NULL,
      PRIMARY KEY (source_id, rel_path)
    );
    CREATE TABLE project_dirs (
      source_id TEXT NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
      rel_path TEXT NOT NULL,
      marker TEXT NOT NULL,
      seen INTEGER NOT NULL,
      PRIMARY KEY (source_id, rel_path)
    );
    CREATE TABLE jobs (
      id TEXT PRIMARY KEY,
      source_id TEXT NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
      title TEXT NOT NULL,
      kind TEXT NOT NULL,
      state TEXT NOT NULL,
      phase TEXT NOT NULL,
      queued_at TEXT NOT NULL,
      position INTEGER NOT NULL,
      discovered INTEGER NOT NULL DEFAULT 0,
      candidates INTEGER NOT NULL DEFAULT 0,
      hashed INTEGER NOT NULL DEFAULT 0,
      succeeded INTEGER NOT NULL DEFAULT 0,
      skipped INTEGER NOT NULL DEFAULT 0,
      errors INTEGER NOT NULL DEFAULT 0,
      error_summary TEXT,
      current_path TEXT,
      current_size INTEGER NOT NULL DEFAULT 0,
      last_checkpoint_at TEXT,
      finished_at TEXT,
      reason TEXT,
      saved INTEGER NOT NULL DEFAULT 0
    );
    CREATE TABLE scan_errors (
      job_id TEXT NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
      path TEXT NOT NULL,
      message TEXT NOT NULL
    );
    CREATE TABLE decisions (
      stack_id TEXT PRIMARY KEY,
      json TEXT NOT NULL,
      status TEXT NOT NULL,
      decided_at TEXT NOT NULL
    );
    """,

    // Version 2: runs of the plan, each item's journal (where things went), restore conflicts, and per-source counts
    // kept by the scanner so the interface never counts the files table.
    """
    ALTER TABLE sources ADD COLUMN indexed_files INTEGER NOT NULL DEFAULT 0;
    ALTER TABLE sources ADD COLUMN cloud_files INTEGER NOT NULL DEFAULT 0;
    ALTER TABLE sources ADD COLUMN regenerable_files INTEGER NOT NULL DEFAULT 0;
    UPDATE sources SET
      indexed_files = (SELECT COUNT(*) FROM files f WHERE f.source_id = sources.id AND f.cloud_only = 0),
      cloud_files = (SELECT COUNT(*) FROM files f WHERE f.source_id = sources.id AND f.cloud_only = 1),
      regenerable_files = (SELECT COALESCE(SUM(files), 0) FROM regenerable_dirs r WHERE r.source_id = sources.id);
    CREATE TABLE batches (
      id TEXT PRIMARY KEY,
      ran_at TEXT NOT NULL,
      emptied_at TEXT
    );
    CREATE TABLE batch_items (
      batch_id TEXT NOT NULL REFERENCES batches(id) ON DELETE CASCADE,
      id TEXT NOT NULL,
      position INTEGER NOT NULL,
      stack_id TEXT NOT NULL,
      kind TEXT NOT NULL,
      target TEXT NOT NULL,
      detail TEXT NOT NULL,
      state TEXT NOT NULL,
      bytes INTEGER NOT NULL,
      file_count INTEGER NOT NULL,
      failed_files INTEGER NOT NULL,
      failure_reason TEXT,
      retried INTEGER NOT NULL,
      recycle_volume TEXT,
      original_path TEXT,
      recycled_path TEXT,
      created_path TEXT,
      source_path TEXT,
      source_stamp TEXT,
      undone_path TEXT,
      manifest TEXT,
      PRIMARY KEY (batch_id, id)
    );
    CREATE TABLE restore_conflicts (
      id TEXT PRIMARY KEY,
      batch_id TEXT NOT NULL REFERENCES batches(id) ON DELETE CASCADE,
      item_id TEXT NOT NULL,
      original_path TEXT NOT NULL,
      recycled_at TEXT NOT NULL,
      recycled_bytes INTEGER NOT NULL,
      recycled_modified TEXT NOT NULL,
      current_created TEXT NOT NULL,
      current_bytes INTEGER NOT NULL,
      resolution TEXT
    );
    """,
  ];

  private IndexDatabase(string path)
  {
    Path = path;
  }

  public string Path { get; }

  /// <summary>Opens the index at <paramref name="path"/>, creating or migrating it as needed.</summary>
  /// <exception cref="InvalidOperationException">The index was written by a newer version of the app.</exception>
  public static IndexDatabase OpenOrCreate(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }
    var database = new IndexDatabase(path);
    database.Migrate();
    return database;
  }

  /// <summary>Opens a connection with WAL, foreign keys and a busy timeout so a reader never fails while a scan writes.</summary>
  public SqliteConnection Open()
  {
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    {
      DataSource = Path,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Pooling = true,
    }.ToString());
    connection.Open();
    Execute(connection, "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
    return connection;
  }

  public static int Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
  {
    ArgumentNullException.ThrowIfNull(connection);
    using var command = Command(connection, sql, parameters);
    return command.ExecuteNonQuery();
  }

  public static T? Scalar<T>(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
  {
    ArgumentNullException.ThrowIfNull(connection);
    using var command = Command(connection, sql, parameters);
    var value = command.ExecuteScalar();
    return value is null or DBNull ? default : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
  }

  public static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
  {
    ArgumentNullException.ThrowIfNull(connection);
    var command = connection.CreateCommand();
#pragma warning disable CA2100 // Every statement is a constant in this assembly; values always go through parameters.
    command.CommandText = sql;
#pragma warning restore CA2100
    foreach (var (name, value) in parameters)
    {
      command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
    return command;
  }

  /// <summary>Runs <paramref name="work"/> in one write transaction; a batch committed this way is a checkpoint.</summary>
  public static void InTransaction(SqliteConnection connection, Action work)
  {
    ArgumentNullException.ThrowIfNull(work);
    Execute(connection, "BEGIN IMMEDIATE");
    try
    {
      work();
      Execute(connection, "COMMIT");
    }
    catch
    {
      Execute(connection, "ROLLBACK");
      throw;
    }
  }

  public string? GetMeta(string key)
  {
    using var connection = Open();
    return Scalar<string>(connection, "SELECT value FROM meta WHERE key = $key", ("$key", key));
  }

  public void SetMeta(string key, string value)
  {
    using var connection = Open();
    Execute(connection, "INSERT INTO meta(key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
      ("$key", key), ("$value", value));
  }

  private void Migrate()
  {
    using var connection = Open();
    var version = Scalar<long>(connection, "PRAGMA user_version");
    if (version == SchemaVersion)
    {
      return;
    }
    if (version > SchemaVersion)
    {
      throw new InvalidOperationException(
        $"The index at '{Path}' has schema version {version}, newer than this app's {SchemaVersion}. Install the newer version of File Merge Assistant.");
    }
    if (version > 0)
    {
      Backup(connection, $"{Path}.v{version}.bak");
    }
    for (var next = (int)version; next < SchemaVersion; next++)
    {
      var migration = Migrations[next];
      var target = (next + 1).ToString(CultureInfo.InvariantCulture);
      InTransaction(connection, () =>
      {
        Execute(connection, migration);
        Execute(connection, "PRAGMA user_version = " + target);
      });
    }
  }

  private static void Backup(SqliteConnection connection, string destination)
  {
    using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
    target.Open();
    connection.BackupDatabase(target);
  }
}
