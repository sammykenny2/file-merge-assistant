using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Scanning;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Workbench;
using Microsoft.Win32;

namespace FileMergeAssistant.Gui.Workbench;

public sealed class ImagePairRow(SimilarImagePair pair, AnalysisSessionFile first, AnalysisSessionFile second) : ObservableObject
{
  public SimilarImagePair Pair { get; } = pair;
  public AnalysisSessionFile First { get; } = first;
  public AnalysisSessionFile Second { get; } = second;
  public string FirstName => Path.GetFileName(First.Path);
  public string SecondName => Path.GetFileName(Second.Path);
  public string Distance => AppText.Get("images.bits", ("bits", Pair.Distance));
  public ImageReviewChoice Choice { get; private set; }
  public string Review => AppText.Get("images.choice." + Choice);
  internal void SetChoice(ImageReviewChoice choice) { Choice = choice; Refresh(); }
  internal void Refresh() { OnPropertyChanged(nameof(Distance)); OnPropertyChanged(nameof(Review)); }
}

public sealed partial class ImageReviewViewModel : PageViewModel, IDisposable
{
  private sealed record JobProgress(ImmutableArray<AnalysisSessionJob> Jobs);
  private sealed class ProgressForwarder(IProgress<JobProgress> target) : IProgress<ImmutableArray<AnalysisSessionJob>>
  {
    public void Report(ImmutableArray<AnalysisSessionJob> value) => target.Report(new(value));
  }
  private readonly ImageReviewArchive _archive;
  private readonly CancellationTokenSource _lifetime = new();
  private ImageReviewDocument? _document;
  private string? _outcome;
  private bool _disposed;
  private long _previewSequence;
  private CancellationTokenSource? _previewCancel;
  private Task _previewTask = Task.CompletedTask;

  [ObservableProperty]
  private int _threshold = SimilarImageAnalysis.DefaultThreshold;
  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
  [NotifyCanExecuteChangedFor(nameof(OpenSavedCommand))]
  [NotifyCanExecuteChangedFor(nameof(MarkRelatedCommand))]
  [NotifyCanExecuteChangedFor(nameof(MarkDifferentCommand))]
  [NotifyCanExecuteChangedFor(nameof(ResetReviewCommand))]
  private bool _busy;
  [ObservableProperty]
  private bool _saving;
  [ObservableProperty]
  private string _status = string.Empty;
  [ObservableProperty]
  private string _scope = string.Empty;
  [ObservableProperty]
  private string _availability = string.Empty;
  [ObservableProperty]
  private string _summary = string.Empty;
  [ObservableProperty]
  private string _evidenceDetails = string.Empty;
  [ObservableProperty]
  private string? _message;
  [ObservableProperty]
  private string? _reportPath;
  [ObservableProperty]
  private ImagePairRow? _selectedPair;
  [ObservableProperty]
  private BitmapSource? _leftPreview;
  [ObservableProperty]
  private BitmapSource? _rightPreview;
  [ObservableProperty]
  private string? _previewError;
  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(MarkRelatedCommand))]
  [NotifyCanExecuteChangedFor(nameof(MarkDifferentCommand))]
  [NotifyCanExecuteChangedFor(nameof(ResetReviewCommand))]
  private bool _previewReady;

  public ImageReviewViewModel(IWorkbenchStore store, INavigator navigator) : base(store, navigator)
  {
    _archive = store.ImageReviews;
    Refresh();
  }

  public IReadOnlyList<int> Distances { get; } = Enumerable.Range(0, SimilarImageAnalysis.MaximumThreshold + 1).ToArray();
  public ObservableCollection<ImagePairRow> Pairs { get; } = [];
  public Task PreviewTask => _previewTask;
  public bool HasPair => SelectedPair is not null;
  public bool CanOpenSaved => !_disposed && !Busy && !Store.IsDemo;
  public bool CanMark => !_disposed && !Busy && PreviewReady && HasPair;
  public bool CanAnalyze => !_disposed && !Busy && !Store.IsDemo && Store.Sources.Any(source => source.State != SourceState.Offline)
    && !Store.Jobs.Any(job => job.IsActive || job.State == ScanJobState.Queued)
    && Store.Engines.Descriptors.Any(engine => engine.Id == NativeAnalysisEngine.EngineId && engine.Availability == AnalysisAvailability.Available
      && engine.Supports(AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8));
  public string LeftPath => SelectedPair?.First.Path ?? string.Empty;
  public string RightPath => SelectedPair?.Second.Path ?? string.Empty;
  public string LeftInfo => SelectedPair is { } pair ? Fmt.Size(pair.First.Observation.Size) : string.Empty;
  public string RightInfo => SelectedPair is { } pair ? Fmt.Size(pair.Second.Observation.Size) : string.Empty;

  public override void Refresh()
  {
    Scope = _document is { } saved ? AppText.Get("images.snapshotScope", ("sources", string.Join(" · ", saved.Sources.Select(source => source.Source.Root))))
      : AppText.Get("images.scope", ("sources", string.Join(" · ", Store.Sources.Where(source => source.State != SourceState.Offline).Select(source => source.RootPath))));
    var native = Store.Engines.Descriptors.FirstOrDefault(engine => engine.Id == NativeAnalysisEngine.EngineId);
    var imageSupport = native?.Availability == AnalysisAvailability.Available
      && native.Supports(AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    var unavailable = native?.Availability is null or AnalysisAvailability.Available ? AnalysisAvailability.Unsupported : native.Availability;
    Availability = imageSupport ? AppText.Get("images.engineReady")
      : AppText.Get("images.engineMissing", ("reason", AppText.Get("analysis.availability." + unavailable)));
    if (!Busy) { Status = AppText.Get("images.outcome." + (_outcome ?? "Ready")); }
    Summary = AppText.Get("images.summary", ("images", Fmt.Count(_document?.Images.Length ?? 0)), ("pairs", Fmt.Count(Pairs.Count)), ("reviewed", Fmt.Count(Pairs.Count(pair => pair.Choice != ImageReviewChoice.Pending))));
    EvidenceDetails = _document is { } document ? AppText.Get("images.evidenceDetails", ("engine", document.Descriptor.Name),
      ("version", document.Engine.EngineVersion), ("algorithm", document.Engine.Algorithm), ("distance", document.Threshold), ("index", document.IndexPath)) : string.Empty;
    foreach (var row in Pairs) { row.Refresh(); }
    AnalyzeCommand.NotifyCanExecuteChanged();
    OpenSavedCommand.NotifyCanExecuteChanged();
  }

  [RelayCommand(CanExecute = nameof(CanAnalyze), IncludeCancelCommand = true)]
  private async Task AnalyzeAsync(CancellationToken cancellationToken)
  {
    if (_disposed || Busy) { return; }
    Busy = true;
    Message = null;
    ClearSelection();
    Pairs.Clear();
    ReportPath = null;
    _document = null;
    Refresh();
    using var progress = new CoalescingProgress<JobProgress>(value =>
      Status = value.Jobs.Length > 0 && value.Jobs.All(job => job.State == ScanJobState.Completed) ? AppText.Get("images.comparing")
        : AppText.Get("images.progress", ("files", Fmt.Count(value.Jobs.Sum(job => job.Discovered))), ("images", Fmt.Count(value.Jobs.Sum(job => job.Confirmed)))));
    try
    {
      var engine = Store.Engines.Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
      var report = await Store.AnalyzeImagesAsync(engine, Threshold, new ProgressForwarder(progress), cancellationToken);
      progress.Dispose();
      Saving = true;
      Status = AppText.Get("images.saving");
      var path = await _archive.CreateAsync(report, CancellationToken.None);
      var document = await _archive.LoadAsync(path, CancellationToken.None);
      if (!_disposed) { ApplyDocument(document, path); }
    }
    catch (OperationCanceledException) { _outcome = "Canceled"; }
    catch (Exception error) when (Expected(error)) { Message = AppText.Get("images.failed", ("error", error.Message)); _outcome = "Partial"; }
    finally
    {
      progress.Dispose();
      Saving = false;
      Busy = false;
      Refresh();
    }
  }

  [RelayCommand(CanExecute = nameof(CanOpenSaved))]
  private async Task OpenSavedAsync()
  {
    var dialog = new OpenFileDialog { InitialDirectory = _archive.DirectoryPath, Filter = AppText.Get("images.fileFilter"),
      Title = AppText.Get("images.open"), CheckFileExists = true };
    if (dialog.ShowDialog() == true) { await OpenArchiveAsync(dialog.FileName); }
  }

  public async Task OpenArchiveAsync(string path)
  {
    if (Busy || _disposed) { return; }
    Busy = true;
    Message = null;
    try
    {
      var document = await _archive.LoadAsync(path, _lifetime.Token);
      if (!_disposed) { ApplyDocument(document, path); }
    }
    catch (OperationCanceledException) when (_disposed) { }
    catch (Exception error) when (Expected(error)) { Message = AppText.Get("images.failed", ("error", error.Message)); }
    finally { Busy = false; Refresh(); }
  }

  private void ApplyDocument(ImageReviewDocument document, string path)
  {
    ClearSelection();
    _document = document;
    ReportPath = path;
    _outcome = document.Outcome;
    Threshold = document.Threshold;
    var files = document.Images.ToDictionary(file => file.Id);
    var choices = document.Decisions.ToDictionary(choice => (choice.FirstId, choice.SecondId), choice => choice.Choice);
    Pairs.Clear();
    foreach (var pair in document.Pairs)
    {
      var row = new ImagePairRow(pair, files[pair.FirstId], files[pair.SecondId]);
      row.SetChoice(choices.GetValueOrDefault((pair.FirstId, pair.SecondId)));
      Pairs.Add(row);
    }
    var reasons = document.Errors.Select(error => error.Path + ": " + error.Message)
      .Concat(document.Jobs.Where(job => !job.Complete && job.Reason is not null).Select(job => job.Reason!));
    Message = document.Error ?? (document.Outcome != "Completed" ? string.Join(Environment.NewLine, reasons.Take(5)) : null);
    if (document.DetailsTruncated) { Message = AppText.Get("images.truncated"); }
    SelectedPair = Pairs.FirstOrDefault();
  }

  partial void OnSelectedPairChanged(ImagePairRow? value)
  {
    OnPropertyChanged(nameof(HasPair));
    OnPropertyChanged(nameof(LeftPath)); OnPropertyChanged(nameof(RightPath));
    OnPropertyChanged(nameof(LeftInfo)); OnPropertyChanged(nameof(RightInfo));
    _previewCancel?.Cancel(); _previewCancel?.Dispose();
    _previewCancel = null;
    var sequence = ++_previewSequence;
    PreviewReady = false;
    LeftPreview = null; RightPreview = null; PreviewError = null;
    if (_disposed) { return; }
    _previewCancel = new CancellationTokenSource();
    if (value is not null && _document is { } document)
    {
      _previewTask = LoadPreviewAfterAsync(_previewTask, document, value, sequence, _previewCancel.Token);
    }
  }

  private async Task LoadPreviewAfterAsync(Task previous, ImageReviewDocument document, ImagePairRow pair, long sequence, CancellationToken cancellationToken)
  {
    try
    {
      await previous; // Serialize decoders; canceled pending selections never allocate another bitmap.
      cancellationToken.ThrowIfCancellationRequested();
      if (_disposed) { return; }
      var result = await Task.Run(() =>
      {
        BitmapSource Read(long id)
        {
          cancellationToken.ThrowIfCancellationRequested();
          using var stream = _archive.OpenPreview(document, id);
          if (stream.Length > 32 * 1024 * 1024) { throw new InvalidDataException("The preview exceeds the image size limit."); }
          var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnDemand);
          var frame = decoder.Frames[0];
          if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 24_000_000)
          { throw new InvalidDataException("The preview exceeds the image dimension limit."); }
          stream.Position = 0;
          var bitmap = new BitmapImage();
          bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
          if (frame.PixelWidth >= frame.PixelHeight) { bitmap.DecodePixelWidth = Math.Min(700, frame.PixelWidth); }
          else { bitmap.DecodePixelHeight = Math.Min(700, frame.PixelHeight); }
          bitmap.StreamSource = stream; bitmap.EndInit();
          if (bitmap.PixelWidth > 700 || bitmap.PixelHeight > 700) { throw new InvalidDataException("The preview decoder exceeded thumbnail bounds."); }
          bitmap.Freeze();
          return bitmap;
        }
        var left = Read(pair.Pair.FirstId);
        var right = Read(pair.Pair.SecondId);
        cancellationToken.ThrowIfCancellationRequested();
        return (left, right);
      }, cancellationToken);
      if (_disposed || sequence != _previewSequence) { return; }
      LeftPreview = result.left; RightPreview = result.right; PreviewReady = true;
    }
    catch (OperationCanceledException) { }
    catch (Exception error) when (Expected(error))
    {
      if (!_disposed && sequence == _previewSequence) { PreviewError = AppText.Get("images.previewFailed", ("error", error.Message)); }
    }
  }

  private void ClearSelection() { SelectedPair = null; PreviewReady = false; }

  [RelayCommand(CanExecute = nameof(CanMark))]
  private Task MarkRelatedAsync() => MarkAsync(ImageReviewChoice.Related);
  [RelayCommand(CanExecute = nameof(CanMark))]
  private Task MarkDifferentAsync() => MarkAsync(ImageReviewChoice.Different);
  [RelayCommand(CanExecute = nameof(CanMark))]
  private Task ResetReviewAsync() => MarkAsync(ImageReviewChoice.Pending);

  private async Task MarkAsync(ImageReviewChoice choice)
  {
    if (SelectedPair is not { } row || _document is null || ReportPath is null || !CanMark) { return; }
    Busy = true;
    Message = null;
    try
    {
      var choices = Pairs.Where(pair => pair != row && pair.Choice != ImageReviewChoice.Pending)
        .Select(pair => new ImagePairDecision(pair.Pair.FirstId, pair.Pair.SecondId, pair.Choice)).ToList();
      if (choice != ImageReviewChoice.Pending) { choices.Add(new(row.Pair.FirstId, row.Pair.SecondId, choice)); }
      var updated = await _archive.SaveDecisionsAsync(ReportPath, _document.Revision, choices.ToImmutableArray());
      row.SetChoice(choice);
      _document = updated;
    }
    catch (Exception error) when (Expected(error)) { Message = AppText.Get("images.failed", ("error", error.Message)); }
    finally { Busy = false; Refresh(); }
  }

  [RelayCommand]
  private void ShowSources() => Navigator.ShowSources();

  private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException
    or ArgumentException or JsonException or DbException or NotSupportedException or FormatException;

  public void Dispose()
  {
    if (_disposed) { return; }
    _disposed = true;
    _lifetime.Cancel(); _lifetime.Dispose();
    AnalyzeCommand.Cancel();
    _previewCancel?.Cancel(); _previewCancel?.Dispose(); _previewCancel = null;
  }
}
