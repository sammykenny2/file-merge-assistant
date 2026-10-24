using FileWorkbench.Scanning;

namespace FileWorkbench.Sources;

public enum ScanJobKind
{
  FirstScan,
  Update,
  Resume,
}

public enum ScanJobState
{
  Running,

  /// <summary>Finishing the current batch before stopping.</summary>
  Pausing,

  Paused,
  Queued,

  /// <summary>Stopped without finishing; saved batches stay valid.</summary>
  Interrupted,

  /// <summary>The source is offline; the job starts after the volume is confirmed.</summary>
  WaitingForSource,

  /// <summary>The exact engine version required by the snapshot is unavailable.</summary>
  WaitingForEngine,

  /// <summary>A legacy or invalid snapshot cannot be resumed safely.</summary>
  NeedsNewJob,

  /// <summary>Cancellation requested; the analysis worker has not acknowledged stopping.</summary>
  Cancelling,

  Failed,

  Completed,
  Canceled,
}

/// <summary>Scan stages from ADR-004. Candidates are only known after discovery and may still grow.</summary>
public enum ScanPhase
{
  Discover,
  Confirm,
  Done,
}

/// <summary>One scan job in the queue. Only one job runs at a time.</summary>
public sealed record ScanJobInfo(
  string Id,
  string SourceId,
  string Title,
  ScanJobKind Kind,
  ScanJobState State,
  ScanPhase Phase,
  DateTimeOffset QueuedAt)
{
  public long Discovered { get; init; }

  /// <summary>Files that share a size with another file and therefore need a content fingerprint.</summary>
  public long Candidates { get; init; }

  public long Hashed { get; init; }
  public long Succeeded { get; init; }
  public long Skipped { get; init; }
  public string? SkippedReason { get; init; }
  public long Errors { get; init; }
  public string? ErrorSummary { get; init; }
  public string? CurrentPath { get; init; }
  public long CurrentSize { get; init; }
  public DateTimeOffset? LastCheckpointAt { get; init; }
  public int CheckpointEvery { get; init; } = 500;
  public DateTimeOffset? FinishedAt { get; init; }

  /// <summary>Why the job exists or what it is waiting for, for example 「上次掃描後多了 46 個檔」.</summary>
  public string? Reason { get; init; }

  /// <summary>Files already saved to the index when the job stopped (interrupted or paused jobs).</summary>
  public long Saved { get; init; }

  public ScanJobSnapshot? Snapshot { get; init; }
  public long AnalysisGeneration { get; init; }
  public bool AnalysisComplete { get; init; }

  public bool IsActive => State is ScanJobState.Running or ScanJobState.Pausing or ScanJobState.Paused or ScanJobState.Cancelling;
  public bool IsFinished => State is ScanJobState.Completed or ScanJobState.Canceled or ScanJobState.Failed;
}
