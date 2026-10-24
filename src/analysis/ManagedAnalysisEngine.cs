using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileWorkbench.Analysis;

public sealed class ManagedAnalysisEngine : IFileAnalysisEngine
{
  public const string EngineId = "csharp";
  public AnalysisEngineDescriptor Descriptor { get; } = new(EngineId, "File Workbench C#", "1.0.0", null, 1, 0,
    RuntimeInformation.RuntimeIdentifier, AnalysisAvailability.Available, null,
    [new(AnalysisCapability.ExactDuplicates, [ContentFingerprints.XxHash128, ContentFingerprints.Sha256], AnalysisMaturity.Experimental)]);

  public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    await Task.Run(() =>
    {
      new AnalysisEngineRegistry([this]).Resolve(request.Engine);
      AnalysisResultValidator.ValidateRequest(request);
      using var parameters = JsonDocument.Parse(request.Engine.ParametersJson);
      if (parameters.RootElement.EnumerateObject().Any()) { throw new ArgumentException("Managed content analysis has no configurable parameters.", nameof(request)); }
    }, cancellationToken).ConfigureAwait(false);
    long sequence = 0, bytes = 0, processed = 0;
    var complete = true;
    foreach (var file in request.Files)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var evidence = await Task.Run(() => AnalyzeFile(file, request.Engine.Algorithm, cancellationToken), cancellationToken).ConfigureAwait(false);
      complete &= evidence.Status == AnalysisFileStatus.Succeeded;
      if (evidence.Status == AnalysisFileStatus.Succeeded) { bytes += file.Observation.Size; }
      processed++;
      yield return new(request.JobId, EngineId, ++sequence, [evidence], [],
        new("Content", processed, request.Files.Length, bytes, file.Path), false, false);
    }
    cancellationToken.ThrowIfCancellationRequested();
    yield return new(request.JobId, EngineId, ++sequence, [], [], new("Done", processed, request.Files.Length, bytes, null), true, complete);
  }

  private static AnalysisFileEvidence AnalyzeFile(AnalysisInput file, string algorithm, CancellationToken cancellationToken)
  {
    try
    {
      var info = new FileInfo(file.Path);
      if (!info.Exists) { throw new FileNotFoundException("The analysis input no longer exists.", file.Path); }
      const FileAttributes unsafeToRead = FileAttributes.ReparsePoint | FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;
      if ((info.Attributes & unsafeToRead) != 0)
      {
        return new(file.Id, file.Observation, AnalysisFileStatus.Skipped, null, "The input became a link or cloud-only placeholder.");
      }
      var before = FileObservation.Capture(file.Path);
      if (!file.Observation.Matches(before)) { return Changed(file, before); }
      var fingerprint = ContentFingerprints.Compute(file.Path, algorithm, cancellationToken);
      var after = FileObservation.Capture(file.Path);
      return before.Matches(after) ? new(file.Id, after, AnalysisFileStatus.Succeeded, fingerprint, null) : Changed(file, after);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      return new(file.Id, file.Observation, AnalysisFileStatus.Failed, null, error.Message);
    }
  }

  private static AnalysisFileEvidence Changed(AnalysisInput file, FileObservation observed) =>
    new(file.Id, observed, AnalysisFileStatus.Changed, null, "The file changed since the analysis request was created.");
}
