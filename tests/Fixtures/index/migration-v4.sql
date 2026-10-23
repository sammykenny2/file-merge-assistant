-- Archived migration 4 from commit c988a94.
ALTER TABLE jobs ADD COLUMN analysis_snapshot TEXT;
    ALTER TABLE jobs ADD COLUMN analysis_generation INTEGER NOT NULL DEFAULT 0;
    ALTER TABLE jobs ADD COLUMN analysis_complete INTEGER NOT NULL DEFAULT 0;
    UPDATE jobs SET state = 'NeedsNewJob', reason = 'Legacy job has no engine/scope snapshot. Create a new scan.'
      WHERE state NOT IN ('Completed', 'Canceled');
    CREATE TABLE file_fingerprints (
      file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
      algorithm TEXT NOT NULL,
      parameters TEXT NOT NULL,
      hash TEXT NOT NULL,
      size INTEGER NOT NULL,
      mtime INTEGER NOT NULL,
      file_identity TEXT,
      engine_id TEXT NOT NULL,
      engine_version TEXT NOT NULL,
      upstream_commit TEXT,
      job_id TEXT,
      generation INTEGER NOT NULL,
      observed_at TEXT NOT NULL,
      PRIMARY KEY (file_id, algorithm, parameters)
    );
    INSERT INTO file_fingerprints(file_id, algorithm, parameters, hash, size, mtime, engine_id, engine_version, generation, observed_at)
      SELECT id, 'xxh128-v1', '{}', hash, size, mtime, 'legacy-unattributed', '0', 0, 'unknown'
      FROM files WHERE hash LIKE 'xxh128:%' AND length(hash) = 39
        AND substr(hash, 8) NOT GLOB '*[^0-9a-f]*';
    CREATE TABLE analysis_results (
      job_id TEXT NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
      generation INTEGER NOT NULL,
      file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
      hash TEXT NOT NULL,
      size INTEGER NOT NULL,
      mtime INTEGER NOT NULL,
      PRIMARY KEY (job_id, generation, file_id)
    );
PRAGMA user_version = 4;
