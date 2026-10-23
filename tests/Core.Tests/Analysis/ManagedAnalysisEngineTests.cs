using System.Collections.Immutable;
using FileMergeAssistant.Analysis;
using Xunit;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class ManagedAnalysisEngineTests
{
  internal static AnalysisRequest Request(KnownAnswerDataset dataset, string algorithm = ContentFingerprints.XxHash128) =>
    new("known-answer", AnalysisEngineRegistry.ManagedOnly().Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, algorithm),
      dataset.Files.Select(file => new AnalysisInput(file.Id, "fixture", file.Path, FileObservation.Capture(file.Path))).ToImmutableArray());

  [Theory]
  [InlineData(ContentFingerprints.XxHash128)]
  [InlineData(ContentFingerprints.Sha256)]
  public async Task Known_Answers_Have_Full_Per_File_Coverage_And_No_Source_Changes(string algorithm)
  {
    using var dataset = new KnownAnswerDataset();
    var request = Request(dataset, algorithm);
    var validator = new AnalysisResultValidator(request);
    var evidence = new List<AnalysisFileEvidence>();
    await foreach (var batch in new ManagedAnalysisEngine().AnalyzeAsync(request))
    {
      validator.Accept(batch);
      evidence.AddRange(batch.Files);
      if (batch.IsFinal) { Assert.True(batch.IsComplete); }
    }
    validator.RequireFinal();
    Assert.Equal(7, evidence.Count);
    Assert.All(evidence, file => Assert.Equal(AnalysisFileStatus.Succeeded, file.Status));
    var groups = evidence.GroupBy(file => file.Fingerprint, StringComparer.Ordinal).Where(group => group.Count() > 1)
      .Select(group => string.Join(',', group.Select(file => file.FileId).Order())).Order(StringComparer.Ordinal);
    Assert.Equal(dataset.ExactGroups.Select(group => string.Join(',', group)).Order(StringComparer.Ordinal), groups);
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [Fact]
  public async Task Changed_And_Missing_Files_Are_Accounted_For_Without_Fingerprints()
  {
    using var dataset = new KnownAnswerDataset();
    var request = Request(dataset);
    File.WriteAllText(dataset.Files[0].Path, "changed-after-request");
    File.Delete(dataset.Files[1].Path);
    var validator = new AnalysisResultValidator(request);
    var results = new List<AnalysisFileEvidence>();
    await foreach (var batch in new ManagedAnalysisEngine().AnalyzeAsync(request))
    {
      validator.Accept(batch);
      results.AddRange(batch.Files);
      if (batch.IsFinal) { Assert.False(batch.IsComplete); }
    }
    validator.RequireFinal();
    Assert.Equal(AnalysisFileStatus.Changed, results.Single(file => file.FileId == 101).Status);
    Assert.Equal(AnalysisFileStatus.Failed, results.Single(file => file.FileId == 102).Status);
    Assert.All(results.Where(file => file.Status != AnalysisFileStatus.Succeeded), file => Assert.Null(file.Fingerprint));
  }

  [Fact]
  public async Task Cancellation_Between_Batches_Does_Not_Pretend_To_Complete()
  {
    using var dataset = new KnownAnswerDataset();
    using var cancel = new CancellationTokenSource();
    var request = Request(dataset);
    var validator = new AnalysisResultValidator(request);
    await using var results = new ManagedAnalysisEngine().AnalyzeAsync(request, cancel.Token).GetAsyncEnumerator();
    Assert.True(await results.MoveNextAsync());
    validator.Accept(results.Current);
    cancel.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await results.MoveNextAsync());
    Assert.Throws<InvalidDataException>(validator.RequireFinal);
  }

  [Fact]
  public async Task Unsupported_Capabilities_And_Unknown_Parameters_Do_Not_Return_Empty_Success()
  {
    using var dataset = new KnownAnswerDataset();
    var request = Request(dataset);
    var unsupported = request with { Engine = request.Engine with { Capability = AnalysisCapability.SimilarImages } };
    await using var results = new ManagedAnalysisEngine().AnalyzeAsync(unsupported).GetAsyncEnumerator();
    var error = await Assert.ThrowsAsync<AnalysisEngineUnavailableException>(async () => await results.MoveNextAsync());
    Assert.Equal(AnalysisAvailability.Unsupported, error.Availability);
    var ignoredParameter = request with { Engine = request.Engine with { ParametersJson = "{\"ignored\":true}" } };
    await using var configured = new ManagedAnalysisEngine().AnalyzeAsync(ignoredParameter).GetAsyncEnumerator();
    await Assert.ThrowsAsync<ArgumentException>(async () => await configured.MoveNextAsync());
  }
}
