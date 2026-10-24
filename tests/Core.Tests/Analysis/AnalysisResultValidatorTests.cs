using FileWorkbench.Analysis;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

public sealed class AnalysisResultValidatorTests
{
  private static AnalysisBatch First(AnalysisRequest request) => new(request.JobId, request.Engine.EngineId, 1,
    [new(request.Files[0].Id, request.Files[0].Observation, AnalysisFileStatus.Succeeded,
      ContentFingerprints.Compute(request.Files[0].Path, request.Engine.Algorithm, CancellationToken.None), null)], [],
    new("Content", 1, request.Files.Length, request.Files[0].Observation.Size, request.Files[0].Path), false, false);

  [Fact]
  public void Rejects_Unknown_Duplicate_Changed_Or_Mislabelled_Evidence_Atomically()
  {
    using var dataset = new KnownAnswerDataset();
    var request = ManagedAnalysisEngineTests.Request(dataset);
    var valid = First(request);
    var validator = new AnalysisResultValidator(request);
    Assert.Throws<InvalidDataException>(() => validator.Accept(valid with { Files = [valid.Files[0] with { FileId = 999 }] }));
    Assert.Throws<InvalidDataException>(() => validator.Accept(valid with { Files = [valid.Files[0], valid.Files[0]] }));
    Assert.Throws<InvalidDataException>(() => validator.Accept(valid with { Files = [valid.Files[0] with { Observation = valid.Files[0].Observation with { Size = 99 } }] }));
    Assert.Throws<InvalidDataException>(() => validator.Accept(valid with { Files = [valid.Files[0] with { Fingerprint = "blake3-v1:" + new string('a', 64) }] }));
    Assert.Throws<InvalidDataException>(() => validator.Accept(valid with { Files = [valid.Files[0] with
      { Status = AnalysisFileStatus.Changed, Fingerprint = null, Error = "changed", Observation = new(-1, 0) }] }));
    Assert.Throws<InvalidDataException>(() => validator.Accept(valid with { Files = [valid.Files[0] with
      { Status = AnalysisFileStatus.Changed, Fingerprint = null, Error = "changed", Observation = new(1, long.MaxValue) }] }));
    validator.Accept(valid);
    Assert.Throws<InvalidDataException>(() => validator.Accept(valid with { Sequence = 2 }));
    Assert.Throws<InvalidDataException>(validator.RequireFinal);
  }

  [Fact]
  public void Rejects_False_Coverage_Progress_And_Exact_Groups_Without_Equal_Evidence()
  {
    using var dataset = new KnownAnswerDataset();
    var request = ManagedAnalysisEngineTests.Request(dataset);
    var batch = First(request);
    var validator = new AnalysisResultValidator(request);
    Assert.Throws<InvalidDataException>(() => validator.Accept(batch with { IsFinal = true, IsComplete = true }));
    Assert.Throws<InvalidDataException>(() => validator.Accept(batch with { Progress = batch.Progress with { Processed = 2 } }));
    Assert.Throws<InvalidDataException>(() => validator.Accept(batch with { EngineId = "another-engine" }));
    Assert.Throws<InvalidDataException>(() => validator.Accept(batch with { Groups = [new(AnalysisCapability.ExactDuplicates, [101, 103], null, null)] }));
    validator.Accept(batch);
  }

  [Fact]
  public void Snapshots_Are_Version_Pinned_And_Requests_Require_Unique_Explicit_Files()
  {
    using var dataset = new KnownAnswerDataset();
    var request = ManagedAnalysisEngineTests.Request(dataset);
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var error = Assert.Throws<AnalysisEngineUnavailableException>(() => registry.Resolve(request.Engine with { EngineVersion = "missing-version" }));
    Assert.Equal(AnalysisAvailability.Incompatible, error.Availability);
    Assert.Throws<AnalysisEngineUnavailableException>(() => registry.Snapshot("missing", AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128));
    Assert.Throws<ArgumentException>(() => AnalysisResultValidator.ValidateRequest(request with { Files = [request.Files[0], request.Files[0]] }));
    Assert.Throws<ArgumentException>(() => AnalysisResultValidator.ValidateRequest(request with { Files = [request.Files[0] with { Path = "relative.txt" }] }));
  }
}
