using System.Runtime.CompilerServices;
using FileMergeAssistant.Analysis;

namespace FileMergeAssistant.AcceptanceHost;

internal sealed class MeteredEngine(IFileAnalysisEngine inner) : IFileAnalysisEngine
{
  private long _files;
  private long _reportedBytes;
  public long Files => Interlocked.Read(ref _files);
  public long ReportedBytes => Interlocked.Read(ref _reportedBytes);
  public AnalysisEngineDescriptor Descriptor => inner.Descriptor;
  public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    Interlocked.Add(ref _files, request.Files.Length);
    long lastBytes = 0;
    await foreach (var batch in inner.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false))
    {
      Interlocked.Add(ref _reportedBytes, batch.Progress.BytesRead - lastBytes);
      lastBytes = batch.Progress.BytesRead;
      yield return batch;
    }
  }
}
