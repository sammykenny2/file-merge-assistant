using FileWorkbench.Plans;
using FileWorkbench.Sources;
using FileWorkbench.Stacks;
using FileWorkbench.Analysis;
using FileWorkbench.Scanning;

namespace FileWorkbench.Workbench;

/// <summary>What a <see cref="IWorkbenchStore.Changed"/> notification covers.</summary>
public sealed class WorkbenchChangedEventArgs : EventArgs
{
  public static readonly WorkbenchChangedEventArgs Everything = new();
  public static readonly WorkbenchChangedEventArgs Progress = new() { OnlyProgress = true };

  /// <summary>Only sources and scan jobs changed (scan progress); stacks, decisions and the plan are as before.</summary>
  public bool OnlyProgress { get; init; }
}

/// <summary>Options applied to every source added from the first-run screen.</summary>
/// <param name="SkipRegenerable">Measure regenerable folders instead of comparing them file by file.</param>
/// <param name="SkipCloudOnly">Never download cloud-only placeholders for comparison.</param>
/// <param name="ProtectedPaths">Paths to mark as protected.</param>
public sealed record SourceOptions(bool SkipRegenerable, bool SkipCloudOnly, IReadOnlyList<string> ProtectedPaths);

/// <summary>
/// Everything the workbench screens read and change: sources and scan jobs, stacks and decisions, the pending plan,
/// the history and the recycle areas. Screens only talk to this interface, so the demo data and the real index are
/// interchangeable.
/// </summary>
/// <remarks>Members are called on the UI thread; <see cref="Changed"/> is raised on the thread that made the change.</remarks>
public interface IWorkbenchStore
{
  /// <summary>Raised after any change.</summary>
  event EventHandler<WorkbenchChangedEventArgs>? Changed;

  /// <summary>True when the store holds simulated data; the interface shows 「模擬資料」.</summary>
  bool IsDemo { get; }

  /// <summary>Current time as the store sees it (fixed in the demo).</summary>
  DateTimeOffset Now { get; }

  IReadOnlyList<SourceInfo> Sources { get; }

  /// <summary>Running, queued, attention-needing and recently finished jobs, in queue order.</summary>
  IReadOnlyList<ScanJobInfo> Jobs { get; }

  AnalysisEngineRegistry Engines { get; }
  AnalysisEngineSnapshot NewJobEngine { get; }
  string? AnalysisConfigurationError { get; }

  /// <summary>Explicit selection for future jobs only; existing snapshots never change.</summary>
  Task SelectNewJobEngineAsync(AnalysisEngineSnapshot snapshot);

  /// <summary>Compares separate runs using frozen current source policies, without changing the main index or plan.</summary>
  Task<AnalysisComparisonReport> CompareEnginesAsync(AnalysisEngineSnapshot first, AnalysisEngineSnapshot second,
    IProgress<AnalysisComparisonProgress>? progress = null, CancellationToken cancellationToken = default);

  ImageReviewArchive ImageReviews { get; }
  Task<SimilarImageReport> AnalyzeImagesAsync(AnalysisEngineSnapshot engine, int threshold,
    IProgress<System.Collections.Immutable.ImmutableArray<AnalysisSessionJob>>? progress = null, CancellationToken cancellationToken = default);

  /// <summary>All known stacks, including pending, converged and kept-all ones.</summary>
  IReadOnlyList<Stack> Stacks { get; }

  /// <summary>Stacks consolidated by an earlier run; they may no longer be listed once only the keeper remains.</summary>
  int ConvergedCount { get; }

  /// <summary>Decided stacks waiting to run, in the order they were added.</summary>
  IReadOnlyList<PlanEntry> Plan { get; }

  /// <summary>When the plan was last re-checked against the disk.</summary>
  DateTimeOffset? PlanCheckedAt { get; }

  /// <summary>Runs of the plan, newest first.</summary>
  IReadOnlyList<ExecutionBatch> History { get; }

  IReadOnlyList<RestoreConflict> RestoreConflicts { get; }

  IReadOnlyList<RecycleArea> RecycleAreas { get; }

  /// <summary>What changed since the user last worked in the app, or null when there is nothing to report.</summary>
  SinceLastVisit? SinceLastVisit { get; }

  /// <summary>The decision recorded for a stack (pending, kept all, or converged), if any.</summary>
  StackDecision? DecisionFor(string stackId);

  /// <summary>The stack with slower facts filled in, such as git status (ADR-005); never changes the stack's identity.</summary>
  Task<Stack> WithDetailsAsync(Stack stack);

  /// <summary>Folders worth offering on the first-run screen.</summary>
  IReadOnlyList<SuggestedLocation> SuggestLocations();

  /// <summary>Adds folders as sources; returns one message per folder that was not added and why.</summary>
  Task<IReadOnlyList<string>> AddSourcesAsync(IReadOnlyList<string> paths, SourceOptions options, bool startScan);

  /// <summary>Queues an update of a source, or resumes its interrupted scan.</summary>
  Task ScanSourceAsync(string sourceId);

  Task PauseJobAsync(string jobId);
  Task ResumeJobAsync(string jobId);
  Task CancelJobAsync(string jobId);

  /// <summary>Starts an interrupted job over instead of resuming it.</summary>
  Task RestartJobAsync(string jobId);

  /// <summary>Moves a queued job up (negative) or down (positive).</summary>
  void MoveJob(string jobId, int offset);

  /// <summary>Re-queues only the files a finished job could not read.</summary>
  Task RescanFailedAsync(string jobId);

  /// <summary>Records a decision. File-changing decisions go to the pending plan; nothing on disk changes.</summary>
  /// <exception cref="InvalidOperationException">The decision is blocked.</exception>
  void Decide(StackDecision decision);

  /// <summary>Adds the suggested decision of each stack to the plan; returns how many were added.</summary>
  int DecideSuggested(IReadOnlyList<string> stackIds);

  /// <summary>Takes a stack out of the plan and puts it back in the list.</summary>
  void Undecide(string stackId);

  void SetPlanItemEnabled(string itemId, bool enabled);

  /// <summary>For an item that changed after the decision: skip it this time instead of stopping.</summary>
  void SetSkipChanged(string itemId, bool skip);

  /// <summary>Checks every plan item against the disk again.</summary>
  Task RecheckPlanAsync();

  /// <summary>Whether this store can run the plan; the real index cannot yet (PLAN-001 Phase 5).</summary>
  bool CanExecute { get; }

  /// <summary>Why the plan cannot run, shown next to the disabled button.</summary>
  string? ExecuteUnavailableReason { get; }

  /// <summary>Runs every item that will run, copies first and recycling after (ADR-002).</summary>
  Task<ExecutionBatch> ExecutePlanAsync(CancellationToken cancellationToken);

  /// <summary>Retries only the failed items of a batch.</summary>
  Task RetryFailedAsync(string batchId);

  Task RestoreItemAsync(string batchId, string itemId);

  /// <summary>Restores every item of a batch: recycled items go back, files copied into keepers are recycled.</summary>
  Task RestoreBatchAsync(string batchId);

  Task ResolveRestoreConflictAsync(string conflictId, RestoreConflictChoice choice, string? folder);

  /// <summary>Permanently deletes what the recycle areas hold; affected batches can no longer be restored.</summary>
  Task EmptyRecycleAreasAsync();

  void RespondToNotice(string noticeId, NoticeResponse response);
}
