using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;

namespace FileMergeAssistant.Gui.Workbench;

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
      SourceState.Scanning => new ChipInfo("掃描中", Tone.Busy),
      SourceState.Offline => new ChipInfo("離線", Tone.Offline),
      SourceState.Interrupted => new ChipInfo("中斷待處理", Tone.Warn),
      SourceState.NeedsUpdate => new ChipInfo("需要更新", Tone.Warn),
      SourceState.Queued => new ChipInfo("等候更新", Tone.Wait),
      _ => new ChipInfo("在線", Tone.Ok),
    };
    (ScanLabel, ScanEnabled) = source.State switch
    {
      SourceState.Scanning => ("掃描中", false),
      SourceState.Offline => ("接上後更新", false),
      SourceState.Interrupted => ("繼續掃描", true),
      SourceState.Queued or SourceState.NeedsUpdate => ("已在佇列", false),
      _ => ("更新", true),
    };

    Indexed = source.State switch
    {
      SourceState.Scanning when job is not null => $"已探索 {Fmt.Count(job.Discovered)} 個檔，正在確認內容",
      SourceState.Interrupted => $"已保存 {Fmt.Count(source.IndexedFiles)} 個檔的索引",
      SourceState.Offline => $"{(source.LastSeenAt is { } seen ? Fmt.Day(seen, now) : "上次")} 的索引：{Fmt.Count(source.IndexedFiles)} 個檔",
      _ => IndexedLine(source),
    };

    var lastScan = source.LastScanAt is { } at
      ? $"最近掃描：{Fmt.When(at, now)}{(source.LastScanErrors > 0 ? $"，有 {source.LastScanErrors} 個錯誤" : string.Empty)}"
      : "還沒有掃描";
    When = source.State switch
    {
      SourceState.Offline => $"最後看到：{(source.LastSeenAt is { } seen ? Fmt.WhenWithTime(seen, now) : "不明")}",
      SourceState.Interrupted or SourceState.NeedsUpdate or SourceState.Scanning => source.Remark ?? lastScan,
      _ => source.Remark is null ? lastScan : $"{lastScan}；{source.Remark}",
    };

    Regenerable = string.Join("、", source.RegenerableNames);
    Exclusions = source.Exclusions.Count == 0 ? "沒有" : string.Join("、", source.Exclusions);
    IsExpanded = source.IsProtected && source.LastScanErrors > 0;
  }

  public string Id { get; }
  public string Name { get; }
  public string Prefix { get; }
  public string Tail { get; }
  public ChipInfo Status { get; }
  public bool IsProtected { get; }
  public ChipInfo ProtectedChip { get; } = new("受保護", Tone.Protected);
  public bool IsOffline { get; }
  public string ScanLabel { get; }
  public bool ScanEnabled { get; }
  public IRelayCommand Scan { get; }
  public string Indexed { get; }
  public string When { get; }
  public string Disk { get; }
  public string Regenerable { get; }
  public string Exclusions { get; }

  public string OfflineNote { get; } =
    "離線時可以查看上次的索引，但不能整理這裡的檔案。重新接上後，會用磁碟區序號與資料夾結構確認是同一顆，就算磁碟代號變了也一樣。";

  [RelayCommand]
  private void ToggleSettings() => IsExpanded = !IsExpanded;

  private static string IndexedLine(SourceInfo source)
  {
    var line = $"已索引 {Fmt.Count(source.IndexedFiles)} 個檔";
    if (source.RegenerableFiles > 0)
    {
      line += $"；可重建資料夾裡 {Fmt.Count(source.RegenerableFiles)} 個檔只記大小";
    }
    if (source.CloudOnlyFiles > 0)
    {
      line += $"；{Fmt.Count(source.CloudOnlyFiles)} 個只在雲端，未比對";
    }
    return line;
  }
}

/// <summary>The job that is running (or pausing, or paused) at the top of the queue.</summary>
public sealed class RunningJobViewModel
{
  public RunningJobViewModel(ScanJobInfo job, DateTimeOffset now, IRelayCommand primary, IRelayCommand cancel)
  {
    Title = job.Title;
    Kind = job.Kind switch
    {
      ScanJobKind.FirstScan => "第一次掃描",
      ScanJobKind.Resume => "繼續上次的掃描",
      _ => "更新",
    };
    State = job.State switch
    {
      ScanJobState.Pausing => new ChipInfo("正在暫停", Tone.Busy),
      ScanJobState.Paused => new ChipInfo("已暫停", Tone.Paused),
      _ => new ChipInfo("執行中", Tone.Busy),
    };
    IsPaused = job.State == ScanJobState.Paused;
    var confirming = job.Phase != ScanPhase.Discover;
    Discover = confirming ? $"完成，找到 {Fmt.Count(job.Discovered)} 個檔" : $"進行中，已找到 {Fmt.Count(job.Discovered)} 個檔";
    Confirm = confirming
      ? $"計算內容指紋：已處理 {Fmt.Count(job.Hashed)} 個，可能是複本的共 {Fmt.Count(job.Candidates)} 個"
      : "等探索完成後開始";
    ConfirmNote = confirming
      ? $"其餘 {Fmt.Count(Math.Max(0, job.Discovered - job.Candidates))} 個檔大小都不重複，只記錄大小與日期。候選數可能隨掃描增加。"
      : "探索時就會先找出大小相同的檔；候選總數要等探索完成才確定。";
    Progress = confirming && job.Candidates > 0 ? Math.Min(1, (double)job.Hashed / job.Candidates) : 0;
    IsConfirming = confirming;
    Save = $"每處理 {Fmt.Count(job.CheckpointEvery)} 個保存一次" + (job.LastCheckpointAt is { } at ? $"；最近一次 {Fmt.Time(at)}" : string.Empty);
    Counts = $"成功 {Fmt.Count(job.Succeeded)}　略過 {Fmt.Count(job.Skipped)}{(job.SkippedReason is null ? string.Empty : $"（{job.SkippedReason}）")}　錯誤 {Fmt.Count(job.Errors)}";
    Current = job.State == ScanJobState.Paused
      ? $"進度已保存到 {Fmt.Count(job.Hashed)} 個。"
      : job.CurrentPath is null ? string.Empty : $"目前：{job.CurrentPath}（{Fmt.Size(job.CurrentSize)}）";
    Note = job.State switch
    {
      ScanJobState.Pausing => "正在把這一批處理完並保存，完成後就會停下。這段時間仍可以瀏覽其他畫面。",
      ScanJobState.Paused => "已暫停，不會再安排新檔案。佇列裡的其他工作也會等這個繼續或取消。",
      _ => null,
    };
    PrimaryLabel = job.State switch
    {
      ScanJobState.Paused => "繼續",
      ScanJobState.Pausing => "正在暫停…",
      _ => "暫停",
    };
    PrimaryEnabled = job.State != ScanJobState.Pausing;
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
  public IRelayCommand Primary { get; }
  public IRelayCommand Cancel { get; }
}

public sealed record QueuedJobViewModel(string Position, string Name, string Description, bool IsFirst, bool IsLast,
  IRelayCommand Up, IRelayCommand Down, IRelayCommand Cancel)
{
  public string UpLabel => $"把「{Name}」往前移";
  public string DownLabel => $"把「{Name}」往後移";
}

public sealed record AttentionJobViewModel(ChipInfo State, string Name, string Description, IReadOnlyList<ActionInfo> Actions);

public sealed partial class FinishedJobViewModel : ObservableObject
{
  [ObservableProperty]
  private bool _showErrors;

  public FinishedJobViewModel(ScanJobInfo job, string sourceName, DateTimeOffset now, IRelayCommand rescan)
  {
    State = job.Errors > 0 ? new ChipInfo($"完成，有 {Fmt.Count(job.Errors)} 個錯誤", Tone.Warn) : new ChipInfo("完成", Tone.Ok);
    Name = sourceName;
    When = job.FinishedAt is { } at ? Fmt.When(at, now) : string.Empty;
    Counts = $"成功 {Fmt.Count(job.Succeeded)}　略過 {Fmt.Count(job.Skipped)}{(job.SkippedReason is null ? string.Empty : $"（{job.SkippedReason}）")}　錯誤 {Fmt.Count(job.Errors)}";
    Summary = job.ErrorSummary ?? job.Reason;
    HasErrors = job.Errors > 0;
    ErrorsLabel = $"查看 {Fmt.Count(job.Errors)} 個錯誤";
    Rescan = rescan;
  }

  public ChipInfo State { get; }
  public string Name { get; }
  public string When { get; }
  public string Counts { get; }
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
    Summary = $"{sources.Count} 個來源，已索引 {Fmt.Count(sources.Sum(s => s.IndexedFiles))} 個檔。"
      + (regenerable > 0 ? $"另有 {Fmt.Count(regenerable)} 個檔在可重建的資料夾裡，只記錄大小。" : string.Empty);

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
        new AsyncRelayCommand(() => Store.CancelJobAsync(running.Id)));

    var queued = Store.Jobs.Where(j => j.State == ScanJobState.Queued).ToList();
    Waiting.Clear();
    for (var i = 0; i < queued.Count; i++)
    {
      var job = queued[i];
      Waiting.Add(new QueuedJobViewModel((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), job.Title,
        $"{KindText(job.Kind)}：{job.Reason ?? "等候中"}", i == 0, i == queued.Count - 1,
        new RelayCommand(() => Store.MoveJob(job.Id, -1)), new RelayCommand(() => Store.MoveJob(job.Id, 1)),
        new AsyncRelayCommand(() => Store.CancelJobAsync(job.Id))));
    }

    Attention.Clear();
    foreach (var job in Store.Jobs.Where(j => j.State is ScanJobState.Interrupted or ScanJobState.WaitingForSource || (j.State == ScanJobState.Paused && j != running)))
    {
      var cancel = new ActionInfo("取消", new AsyncRelayCommand(() => Store.CancelJobAsync(job.Id)));
      Attention.Add(job.State switch
      {
        ScanJobState.Interrupted => new AttentionJobViewModel(new ChipInfo("中斷待處理", Tone.Warn), job.Title, job.Reason ?? string.Empty,
          [new ActionInfo("繼續掃描", new AsyncRelayCommand(() => Store.ResumeJobAsync(job.Id)), true),
           new ActionInfo("從頭重新掃描", new AsyncRelayCommand(() => Store.RestartJobAsync(job.Id))), cancel]),
        ScanJobState.WaitingForSource => new AttentionJobViewModel(new ChipInfo("等待來源", Tone.Offline), job.Title, job.Reason ?? string.Empty,
          [new ActionInfo("我已經接上了", new AsyncRelayCommand(() => Store.ResumeJobAsync(job.Id))), cancel]),
        _ => new AttentionJobViewModel(new ChipInfo("已暫停", Tone.Paused), job.Title, job.Reason ?? string.Empty,
          [new ActionInfo("繼續", new AsyncRelayCommand(() => Store.ResumeJobAsync(job.Id)), true), cancel]),
      });
    }

    Finished.Clear();
    foreach (var job in Store.Jobs.Where(j => j.State == ScanJobState.Completed))
    {
      var name = Store.Sources.FirstOrDefault(s => s.Id == job.SourceId)?.Name ?? job.Title;
      Finished.Add(new FinishedJobViewModel(job, name, now, new AsyncRelayCommand(() => Store.RescanFailedAsync(job.Id))));
    }

    HubLinkLabel = $"到工作台整理已找到的 {Fmt.Count(Store.Stacks.Count(s => s.Status == StackStatus.Open))} 疊";
    OnPropertyChanged(nameof(HasWaiting));
    OnPropertyChanged(nameof(HasAttention));
    OnPropertyChanged(nameof(HasFinished));
  }

  [RelayCommand]
  private void AddFolders() => Navigator.ShowAddSources();

  [RelayCommand]
  private void GoToHub() => Navigator.ShowHub();

  private static string KindText(ScanJobKind kind) => kind switch
  {
    ScanJobKind.FirstScan => "第一次掃描",
    ScanJobKind.Resume => "繼續",
    _ => "更新",
  };
}
