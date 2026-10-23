-- Archived schema 3 from commit 9ba0275 (before PLAN-002 migration).
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
ALTER TABLE batch_items ADD COLUMN keeper_path TEXT;
PRAGMA user_version = 3;
