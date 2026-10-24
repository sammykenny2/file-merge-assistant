using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileWorkbench.Localization;
using FileWorkbench.Sources;
using FileWorkbench.Stacks;
using FileWorkbench.Workbench;

namespace FileWorkbench.Gui.Workbench;

public sealed partial class SourceCardViewModel : ObservableObject
{
  [ObservableProperty]
  private bool _isExpanded;

  public SourceCardViewModel(SourceInfo source, ScanJobInfo? job, DateTimeOffset now, IRelayCommand scan)
  {
    Id = source.Id;
    Name = source.Name;
    (Prefix, Tail) = Fmt.SplitPath(source.RootPath);
    IsProtected = source.IsProtected;
    IsOffline = source.IsOffline;
    Disk = source.VolumeDescription;
    Scan = scan;
    Status = source.State switch
    {
      SourceState.Scanning => new ChipInfo(AppText.Get("sources.state.scanning"), Tone.Busy),
      SourceState.Offline => new ChipInfo(AppText.Get("sources.state.offline"), Tone.Offline),
      SourceState.Interrupted => new ChipInfo(AppText.Get("sources.state.interrupted"), Tone.Warn),
      SourceState.NeedsUpdate => new ChipInfo(AppText.Get("sources.state.needsUpdate"), Tone.Warn),
      SourceState.Queued => new ChipInfo(AppText.Get("sources.state.queued"), Tone.Wait),
      _ => new ChipInfo(AppText.Get("sources.state.online"), Tone.Ok),
    };
    var (scanKey, scanEnabled) = source.State switch
    {
      SourceState.Scanning => ("sources.scan.scanning", false),
      SourceState.Offline => ("sources.scan.whenConnected", false),
      SourceState.Interrupted => ("sources.scan.resume", true),
      SourceState.Queued or SourceState.NeedsUpdate => ("sources.scan.queued", false),
      _ => ("sources.scan.update", true),
    };
    ScanLabel = AppText.Get(scanKey);
    ScanEnabled = scanEnabled;

    var files = Fmt.Count(source.IndexedFiles);
    Indexed = source.State switch
    {
      SourceState.Scanning when job is not null =>
        AppText.Plural("sources.indexed.discovering", job.Discovered, ("count", Fmt.Count(job.Discovered))),
      SourceState.Interrupted => AppText.Plural("sources.indexed.saved", source.IndexedFiles, ("count", files)),
      SourceState.Offline => source.LastSeenAt is { } day
        ? AppText.Plural("sources.indexed.offline", source.IndexedFiles, ("day", Fmt.Day(day, now)), ("count", files))
        : AppText.Plural("sources.indexed.offlineLast", source.IndexedFiles, ("count", files)),
      _ => IndexedLine(source),
    };

    var lastScan = source.LastScanAt is { } at
      ? source.LastScanErrors > 0
        ? AppText.Plural("sources.lastScanErrors", source.LastScanErrors, ("when", Fmt.When(at, now)), ("count", source.LastScanErrors))
        : AppText.Get("sources.lastScan", ("when", Fmt.When(at, now)))
      : AppText.Get("sources.neverScanned");
    When = source.State switch
    {
      SourceState.Offline => source.LastSeenAt is { } seen
        ? AppText.Get("sources.lastSeen", ("when", Fmt.WhenWithTime(seen, now)))
        : AppText.Get("sources.lastSeenUnknown"),
      SourceState.Interrupted or SourceState.NeedsUpdate or SourceState.Scanning => source.Remark ?? lastScan,
      _ => Fmt.Clauses(lastScan, source.Remark),
    };

    Regenerable = Fmt.List(source.RegenerableNames);
    Exclusions = source.Exclusions.Count == 0 ? AppText.Get("sources.card.noExclusions") : Fmt.List(source.Exclusions);
    IsExpanded = source.IsProtected && source.LastScanErrors > 0;
  }

  public string Id { get; }
  public string Name { get; }
  public string Prefix { get; }
  public string Tail { get; }
  public ChipInfo Status { get; }
  public bool IsProtected { get; }
  public ChipInfo ProtectedChip { get; } = new(AppText.Get("sources.card.protected"), Tone.Protected);
  public bool IsOffline { get; }
  public string ScanLabel { get; }
  public bool ScanEnabled { get; }
  public IRelayCommand Scan { get; }
  public string Indexed { get; }
  public string When { get; }
  public string Disk { get; }
  public string Regenerable { get; }
  public string Exclusions { get; }

  public string OfflineNote { get; } = AppText.Get("sources.card.offlineNote");

  [RelayCommand]
  private void ToggleSettings() => IsExpanded = !IsExpanded;

  private static string IndexedLine(SourceInfo source) => Fmt.Clauses(
    AppText.Plural("sources.indexed.files", source.IndexedFiles, ("count", Fmt.Count(source.IndexedFiles))),
    source.RegenerableFiles > 0
      ? AppText.Plural("sources.indexed.regenerable", source.RegenerableFiles, ("count", Fmt.Count(source.RegenerableFiles)))
      : null,
    source.CloudOnlyFiles > 0 ? AppText.Get("sources.indexed.cloudOnly", ("count", Fmt.Count(source.CloudOnlyFiles))) : null);
}

/// <summary>The job that is running (or pausing, or paused) at the top of the queue.</summary>
public sealed class RunningJobViewModel
{
  public RunningJobViewModel(ScanJobInfo job, DateTimeOffset now, IRelayCommand primary, IRelayCommand cancel, bool demo = false)
  {
    Analysis = AnalysisJobText.Summary(job, demo);
    AnalysisDetails = AnalysisJobText.Details(job);
    Title = job.Title;
    Kind = AppText.Get(job.Kind switch
    {
      ScanJobKind.FirstScan => "sources.job.firstScan",
      ScanJobKind.Resume => "sources.job.resumeLast",
      _ => "sources.job.update",
    });
    State = job.State switch
    {
      ScanJobState.Pausing => new ChipInfo(AppText.Get("sources.job.pausing"), Tone.Busy),
      ScanJobState.Cancelling => new ChipInfo(AppText.Get("sources.job.cancelling"), Tone.Busy),
      ScanJobState.Paused => new ChipInfo(AppText.Get("sources.job.paused"), Tone.Paused),
      _ => new ChipInfo(AppText.Get("sources.job.running"), Tone.Busy),
    };
    IsPaused = job.State == ScanJobState.Paused;
    var confirming = job.Phase != ScanPhase.Discover;
    Discover = AppText.Plural(confirming ? "sources.job.discovered" : "sources.job.discovering", job.Discovered, ("count", Fmt.Count(job.Discovered)));
    Confirm = confirming
      ? AppText.Plural("sources.job.fingerprinting", job.Candidates, ("hashed", Fmt.Count(job.Hashed)), ("count", Fmt.Count(job.Candidates)))
      : AppText.Get("sources.job.confirmWaits");
    var unique = Math.Max(0, job.Discovered - job.Candidates);
    ConfirmNote = confirming
      ? AppText.Plural("sources.job.uniqueSizes", unique, ("count", Fmt.Count(unique)))
      : AppText.Get("sources.job.sameSizes");
    Progress = confirming && job.Candidates > 0 ? Math.Min(1, (double)job.Hashed / job.Candidates) : 0;
    IsConfirming = confirming;
    Save = job.LastCheckpointAt is { } at
      ? AppText.Plural("sources.job.saveLast", job.CheckpointEvery, ("count", Fmt.Count(job.CheckpointEvery)), ("time", Fmt.Time(at)))
      : AppText.Plural("sources.job.save", job.CheckpointEvery, ("count", Fmt.Count(job.CheckpointEvery)));
    Counts = CountsText(job);
    Current = job.State == ScanJobState.Paused
      ? AppText.Plural("sources.job.savedTo", job.Hashed, ("count", Fmt.Count(job.Hashed)))
      : job.CurrentPath is null ? string.Empty : AppText.Get("sources.job.current", ("path", job.CurrentPath), ("size", Fmt.Size(job.CurrentSize)));
    Note = job.State switch
    {
      ScanJobState.Pausing => AppText.Get("sources.job.pausingNote"),
      ScanJobState.Paused => AppText.Get("sources.job.pausedNote"),
      _ => null,
    };
    PrimaryLabel = AppText.Get(job.State switch
    {
      ScanJobState.Paused => "sources.job.resume",
      ScanJobState.Pausing => "sources.job.pausingButton",
      _ => "sources.job.pause",
    });
    PrimaryEnabled = job.State is not (ScanJobState.Pausing or ScanJobState.Cancelling);
    Primary = primary;
    Cancel = cancel;
  }

  public string Title { get; }
  public string Kind { get; }
  public ChipInfo State { get; }
  public bool IsPaused { get; }
  public string Discover { get; }
  public string Confirm { get; }
  public string ConfirmNote { get; }
  public double Progress { get; }
  public bool IsConfirming { get; }
  public string Save { get; }
  public string Counts { get; }
  public string Current { get; }
  public string? Note { get; }
  public string PrimaryLabel { get; }
  public bool PrimaryEnabled { get; }
  public string Analysis { get; }
  public string AnalysisDetails { get; }
  public IRelayCommand Primary { get; }
  public IRelayCommand Cancel { get; }

  /// <summary>Succeeded, skipped (with the reason, when there is one) and errors of a job, running or finished.</summary>
  internal static string CountsText(ScanJobInfo job) => AppText.Get(job.SkippedReason is null ? "sources.job.counts" : "sources.job.countsReason",
    ("succeeded", Fmt.Count(job.Succeeded)), ("skipped", Fmt.Count(job.Skipped)), ("reason", job.SkippedReason), ("errors", Fmt.Count(job.Errors)));
}

public sealed record QueuedJobViewModel(string Position, string Name, string Description, bool IsFirst, bool IsLast,
  IRelayCommand Up, IRelayCommand Down, IRelayCommand Cancel)
{
  public string Analysis { get; init; } = string.Empty;
  public string AnalysisDetails { get; init; } = string.Empty;
  public string UpLabel => AppText.Get("sources.waiting.up", ("name", Name));
  public string DownLabel => AppText.Get("sources.waiting.down", ("name", Name));
}

public sealed record AttentionJobViewModel(ChipInfo State, string Name, string Description, IReadOnlyList<ActionInfo> Actions)
{
  public string Analysis { get; init; } = string.Empty;
  public string AnalysisDetails { get; init; } = string.Empty;
}

public sealed partial class FinishedJobViewModel : ObservableObject
{
  [ObservableProperty]
  private bool _showErrors;

  public FinishedJobViewModel(ScanJobInfo job, string sourceName, DateTimeOffset now, IRelayCommand rescan, bool demo = false)
  {
    Analysis = AnalysisJobText.Summary(job, demo);
    AnalysisDetails = AnalysisJobText.Details(job);
    var partial = job.State == ScanJobState.Completed && job.Snapshot is not null && !job.AnalysisComplete;
    ShowErrors = partial;
    State = job.State == ScanJobState.Canceled ? new ChipInfo(AppText.Get("analysis.job.canceled"), Tone.Paused)
      : partial ? new ChipInfo(AppText.Get("analysis.job.partial"), Tone.Warn)
      : job.Errors > 0
      ? new ChipInfo(AppText.Plural("sources.finished.doneErrors", job.Errors, ("count", Fmt.Count(job.Errors))), Tone.Warn)
      : new ChipInfo(AppText.Get("sources.finished.done"), Tone.Ok);
    Name = sourceName;
    When = job.FinishedAt is { } at ? Fmt.When(at, now) : string.Empty;
    Counts = RunningJobViewModel.CountsText(job);
    Summary = job.ErrorSummary ?? job.Reason;
    if (partial) { Summary = AppText.Get("analysis.job.partialHint") + " " + Summary; }
    HasErrors = job.Errors > 0;
    ErrorsLabel = AppText.Plural("sources.finished.viewErrors", job.Errors, ("count", Fmt.Count(job.Errors)));
    Rescan = rescan;
  }

  public ChipInfo State { get; }
  public string Name { get; }
  public string When { get; }
  public string Counts { get; }
  public string Analysis { get; }
  public string AnalysisDetails { get; }
  public string? Summary { get; }
  public bool HasErrors { get; }
  public string ErrorsLabel { get; }
  public IRelayCommand Rescan { get; }

  [RelayCommand]
  private void ToggleErrors() => ShowErrors = !ShowErrors;
}

/// <summary>Screen 2, 「來源與掃描工作」.</summary>
public sealed partial class SourcesViewModel : PageViewModel
{
  private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
  private bool _initialised;

  [ObservableProperty]
  private string _summary = string.Empty;

  [ObservableProperty]
  private RunningJobViewModel? _running;

  [ObservableProperty]
  private string _hubLinkLabel = string.Empty;

  public SourcesViewModel(IWorkbenchStore store, INavigator navigator)
    : base(store, navigator)
  {
    Refresh();
  }

  public ObservableCollection<SourceCardViewModel> Sources { get; } = [];
  public ObservableCollection<QueuedJobViewModel> Waiting { get; } = [];
  public ObservableCollection<AttentionJobViewModel> Attention { get; } = [];
  public ObservableCollection<FinishedJobViewModel> Finished { get; } = [];

  public bool HasWaiting => Waiting.Count > 0;
  public bool HasAttention => Attention.Count > 0;
  public bool HasFinished => Finished.Count > 0;

  public override void Refresh()
  {
    var now = Store.Now;
    foreach (var card in Sources.Where(c => c.IsExpanded))
    {
      _expanded.Add(card.Id);
    }
    foreach (var card in Sources.Where(c => !c.IsExpanded))
    {
      _expanded.Remove(card.Id);
    }

    var sources = Store.Sources;
    var regenerable = sources.Sum(s => s.RegenerableFiles);
    var indexed = sources.Sum(s => s.IndexedFiles);
    Summary = Fmt.Sentences(
      AppText.Plural("sources.summary", sources.Count, ("count", sources.Count),
        ("files", AppText.Plural("sources.files", indexed, ("count", Fmt.Count(indexed))))),
      regenerable > 0 ? AppText.Plural("sources.summary.regenerable", regenerable, ("count", Fmt.Count(regenerable))) : null);

    Sources.Clear();
    foreach (var source in sources)
    {
      var job = Store.Jobs.FirstOrDefault(j => j.SourceId == source.Id && j.IsActive);
      var id = source.Id;
      var card = new SourceCardViewModel(source, job, now, new AsyncRelayCommand(() => Store.ScanSourceAsync(id)));
      if (_initialised)
      {
        card.IsExpanded = _expanded.Contains(source.Id);
      }
      Sources.Add(card);
    }
    _initialised = true;

    var running = Store.Jobs.FirstOrDefault(j => j.IsActive);
    Running = running is null
      ? null
      : new RunningJobViewModel(running, now,
        new AsyncRelayCommand(() => running.State == ScanJobState.Paused ? Store.ResumeJobAsync(running.Id) : Store.PauseJobAsync(running.Id)),
        new AsyncRelayCommand(() => Store.CancelJobAsync(running.Id)), Store.IsDemo);

    var queued = Store.Jobs.Where(j => j.State == ScanJobState.Queued).ToList();
    Waiting.Clear();
    for (var i = 0; i < queued.Count; i++)
    {
      var job = queued[i];
      var description = job.Reason is { } reason
        ? AppText.Get("sources.waiting.description", ("kind", KindText(job.Kind)), ("reason", reason))
        : AppText.Get("sources.waiting.noReason", ("kind", KindText(job.Kind)));
      Waiting.Add(new QueuedJobViewModel((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), job.Title,
        description, i == 0, i == queued.Count - 1,
        new RelayCommand(() => Store.MoveJob(job.Id, -1)), new RelayCommand(() => Store.MoveJob(job.Id, 1)),
        new AsyncRelayCommand(() => Store.CancelJobAsync(job.Id)))
      {
        Analysis = AnalysisJobText.Summary(job, Store.IsDemo), AnalysisDetails = AnalysisJobText.Details(job),
      });
    }

    Attention.Clear();
    foreach (var job in Store.Jobs.Where(j => j.State is ScanJobState.Interrupted or ScanJobState.WaitingForSource or ScanJobState.WaitingForEngine or ScanJobState.NeedsNewJob or ScanJobState.Failed || (j.State == ScanJobState.Paused && j != running)))
    {
      var cancel = new ActionInfo(AppText.Get("sources.cancel"), new AsyncRelayCommand(() => Store.CancelJobAsync(job.Id)));
      Attention.Add((job.State switch
      {
        ScanJobState.Interrupted => new AttentionJobViewModel(new ChipInfo(AppText.Get("sources.state.interrupted"), Tone.Warn), job.Title, job.Reason ?? string.Empty,
          [new ActionInfo(AppText.Get("sources.scan.resume"), new AsyncRelayCommand(() => Store.ResumeJobAsync(job.Id)), true),
           new ActionInfo(AppText.Get("sources.attention.restart"), new AsyncRelayCommand(() => Store.RestartJobAsync(job.Id))), cancel]),
        ScanJobState.WaitingForSource => new AttentionJobViewModel(new ChipInfo(AppText.Get("sources.attention.waitingForSource"), Tone.Offline), job.Title,
          job.Reason ?? string.Empty, [new ActionInfo(AppText.Get("sources.attention.connected"), new AsyncRelayCommand(() => Store.ResumeJobAsync(job.Id))), cancel]),
        ScanJobState.WaitingForEngine or ScanJobState.Failed => new AttentionJobViewModel(
          new ChipInfo(AppText.Get(job.State == ScanJobState.WaitingForEngine ? "sources.state.waitingForEngine" : "sources.state.failed"), Tone.Warn), job.Title,
          job.Reason ?? string.Empty,
          [new ActionInfo(AppText.Get("sources.scan.resume"), new AsyncRelayCommand(() => Store.ResumeJobAsync(job.Id)), true),
           new ActionInfo(AppText.Get("sources.attention.restart"), new AsyncRelayCommand(() => Store.RestartJobAsync(job.Id))), cancel]),
        ScanJobState.NeedsNewJob => new AttentionJobViewModel(new ChipInfo(AppText.Get("sources.state.needsNewJob"), Tone.Warn), job.Title,
          job.Reason ?? string.Empty, [new ActionInfo(AppText.Get("sources.attention.restart"), new AsyncRelayCommand(() => Store.RestartJobAsync(job.Id)), true), cancel]),
        _ => new AttentionJobViewModel(new ChipInfo(AppText.Get("sources.job.paused"), Tone.Paused), job.Title, job.Reason ?? string.Empty,
          [new ActionInfo(AppText.Get("sources.job.resume"), new AsyncRelayCommand(() => Store.ResumeJobAsync(job.Id)), true), cancel]),
      }) with { Analysis = AnalysisJobText.Summary(job, Store.IsDemo), AnalysisDetails = AnalysisJobText.Details(job) });
    }

    Finished.Clear();
    foreach (var job in Store.Jobs.Where(j => j.State is ScanJobState.Completed or ScanJobState.Canceled).OrderByDescending(job => job.FinishedAt).Take(20))
    {
      var name = Store.Sources.FirstOrDefault(s => s.Id == job.SourceId)?.Name ?? job.Title;
      Finished.Add(new FinishedJobViewModel(job, name, now, new AsyncRelayCommand(() => Store.RescanFailedAsync(job.Id)), Store.IsDemo));
    }

    var open = Store.Stacks.Count(s => s.Status == StackStatus.Open);
    HubLinkLabel = AppText.Plural("sources.hubLink", open, ("count", Fmt.Count(open)));
    OnPropertyChanged(nameof(HasWaiting));
    OnPropertyChanged(nameof(HasAttention));
    OnPropertyChanged(nameof(HasFinished));
  }

  [RelayCommand]
  private void AddFolders() => Navigator.ShowAddSources();

  [RelayCommand]
  private void GoToHub() => Navigator.ShowHub();

  private static string KindText(ScanJobKind kind) => AppText.Get(kind switch
  {
    ScanJobKind.FirstScan => "sources.job.firstScan",
    ScanJobKind.Resume => "sources.job.resume",
    _ => "sources.job.update",
  });
}
