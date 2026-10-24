using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FileMergeAssistant.Analysis;

public sealed class NativeAnalysisEngine : IFileAnalysisEngine
{
  public const string EngineId = "czkawka";
  public const string UpstreamCommit = "eb8b91dbb4d25dff416202674ee0de84ee4b5e5c";
  public const string Version = "0.2.0";
  private readonly NativeAnalysisApi? _api;

  /// <param name="nativeDirectory">Trusted installation directory, never a scanned source or working-directory search.</param>
  public NativeAnalysisEngine(string? nativeDirectory = null)
  {
    nativeDirectory ??= NativeAnalysisPaths.Resolve(AppContext.BaseDirectory);
    if (!Path.IsPathFullyQualified(nativeDirectory)) { throw new ArgumentException("The native installation directory must be absolute.", nameof(nativeDirectory)); }
    Descriptor = new(EngineId, "Czkawka", Version, UpstreamCommit, 1, 1, RuntimeInformation.RuntimeIdentifier,
      AnalysisAvailability.MissingDependency, "The optional Czkawka native library is not installed.",
      [new(AnalysisCapability.ExactDuplicates, [ContentFingerprints.Blake3], AnalysisMaturity.Experimental),
        new(AnalysisCapability.SimilarImages, [PerceptualFingerprints.Mean8D8], AnalysisMaturity.Experimental)]);
    if (RuntimeInformation.OSArchitecture is not (Architecture.X64 or Architecture.Arm64))
    {
      Descriptor = Descriptor with { Availability = AnalysisAvailability.Unsupported, UnavailableReason = "No native adapter is provided for this architecture." };
      return;
    }
    var libraryPath = Path.Combine(nativeDirectory, NativeAnalysisPaths.LibraryName);
    if (!File.Exists(libraryPath)) { return; }
    try
    {
      _api = NativeAnalysisApi.Load(libraryPath);
      Descriptor = _api.Descriptor;
    }
    catch (Exception error) when (error is DllNotFoundException or FileNotFoundException)
    {
      Descriptor = Descriptor with { UnavailableReason = error.Message };
    }
    catch (Exception error) when (error is BadImageFormatException or EntryPointNotFoundException or InvalidDataException or JsonException)
    {
      Descriptor = Descriptor with { Availability = AnalysisAvailability.Incompatible, UnavailableReason = error.Message };
    }
  }

  public AnalysisEngineDescriptor Descriptor { get; }

  public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    var api = _api ?? throw new AnalysisEngineUnavailableException(Descriptor.Availability, Descriptor.UnavailableReason!);
    await using var session = await Task.Run(() => Prepare(request, api, cancellationToken), cancellationToken).ConfigureAwait(false);
    var recheckFailed = false;
    while (true)
    {
      var wire = await Task.Run(() => api.Poll(session.Job.Id), CancellationToken.None).ConfigureAwait(false);
      if (wire.Cancelled) { throw new OperationCanceledException("Native analysis was cancelled.", cancellationToken); }
      if (cancellationToken.IsCancellationRequested)
      {
        if (wire.Batch?.IsFinal == true || wire.Error is not null) { throw new OperationCanceledException(cancellationToken); }
        continue;
      }
      if (wire.Error is not null) { throw new InvalidDataException(wire.Error); }
      var batch = wire.Batch ?? throw new InvalidDataException("The native result has no batch.");
      session.RawValidator.Accept(batch);
      var checkedFiles = ImmutableArray.CreateBuilder<AnalysisFileEvidence>(batch.Files.Length);
      foreach (var evidence in batch.Files)
      {
        var checkedFile = await Task.Run(() => Reobserve(evidence, session.Inputs[evidence.FileId]), CancellationToken.None).ConfigureAwait(false);
        recheckFailed |= checkedFile.Status != evidence.Status;
        checkedFiles.Add(checkedFile);
      }
      batch = batch with { Files = checkedFiles.ToImmutable(), IsComplete = batch.IsComplete && !recheckFailed };
      session.Validator.Accept(batch);
      yield return batch;
      if (batch.IsFinal)
      {
        session.RawValidator.RequireFinal();
        session.Validator.RequireFinal();
        yield break;
      }
    }
  }

  private NativeSession Prepare(AnalysisRequest request, NativeAnalysisApi api, CancellationToken cancellationToken)
  {
    new AnalysisEngineRegistry([this]).Resolve(request.Engine);
    AnalysisResultValidator.ValidateRequest(request);
    cancellationToken.ThrowIfCancellationRequested();
    var rawRequest = request with { Files = request.Files.Select(file => file with { Observation = file.Observation with { Identity = null } }).ToImmutableArray() };
    var rawValidator = new AnalysisResultValidator(rawRequest);
    var validator = new AnalysisResultValidator(request);
    var inputs = request.Files.ToDictionary(file => file.Id);
    var session = new NativeSession(api, api.Create(request), rawValidator, validator, inputs, cancellationToken);
    try
    {
      api.AddFiles(session.Job.Id, request.Files, cancellationToken);
      cancellationToken.ThrowIfCancellationRequested();
      api.Start(session.Job.Id);
      session.Started = true;
      return session;
    }
    catch
    {
      session.ReleaseUnstarted();
      throw;
    }
  }

  private static AnalysisFileEvidence Reobserve(AnalysisFileEvidence evidence, AnalysisInput input)
  {
    if (evidence.Status != AnalysisFileStatus.Succeeded) { return evidence; }
    try
    {
      var current = FileObservation.Capture(input.Path);
      return input.Observation.Matches(current) ? evidence with { Observation = current }
        : new(input.Id, current, AnalysisFileStatus.Changed, null, "The file changed before its native result was received.");
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      return new(input.Id, input.Observation, AnalysisFileStatus.Failed, null, error.Message);
    }
  }

  private sealed class NativeAnalysisJob : SafeHandleZeroOrMinusOneIsInvalid
  {
    private readonly NativeAnalysisApi _api;
    public ulong Id { get; }

    public NativeAnalysisJob(NativeAnalysisApi api, ulong id) : base(true)
    {
      _api = api;
      Id = id;
      SetHandle(new IntPtr(checked((long)id)));
    }

    public void Cancel() => _api.Cancel(Id);
    protected override bool ReleaseHandle() => _api.Destroy(Id);
  }

  private sealed class NativeSession : IAsyncDisposable
  {
    private readonly NativeAnalysisApi _api;
    private readonly CancellationTokenRegistration _cancel;
    public NativeAnalysisJob Job { get; }
    public bool Started { get; set; }
    public AnalysisResultValidator RawValidator { get; }
    public AnalysisResultValidator Validator { get; }
    public Dictionary<long, AnalysisInput> Inputs { get; }

    public NativeSession(NativeAnalysisApi api, ulong id, AnalysisResultValidator rawValidator, AnalysisResultValidator validator,
      Dictionary<long, AnalysisInput> inputs, CancellationToken cancellationToken)
    {
      _api = api;
      Job = new(api, id);
      RawValidator = rawValidator;
      Validator = validator;
      Inputs = inputs;
      _cancel = cancellationToken.UnsafeRegister(static state =>
      {
        if (((WeakReference<NativeAnalysisJob>)state!).TryGetTarget(out var job)) { job.Cancel(); }
      }, new WeakReference<NativeAnalysisJob>(Job));
    }

    public async ValueTask DisposeAsync()
    {
      Job.Cancel();
      try
      {
        while (Started && !_api.IsFinished(Job.Id)) { await Task.Delay(10, CancellationToken.None).ConfigureAwait(false); }
      }
      finally { ReleaseUnstarted(); }
    }

    public void ReleaseUnstarted()
    {
      _cancel.Dispose();
      Job.Dispose();
    }
  }
}
