using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Tests.Scanning;
using FileMergeAssistant.Index;
using FileMergeAssistant.Scanning;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class PerceptualAnalysisTests
{
  private static readonly JsonSerializerOptions CorpusJson = new() { PropertyNameCaseInsensitive = true };
  [Theory]
  [InlineData("")]
  [InlineData("0000000000000000")]
  [InlineData("ffffffffffffffff")]
  [InlineData("123456789abcdefG")]
  [InlineData("123456789ABCDEF0")]
  [InlineData("123456789abcdef0,123456789abcdef0")]
  [InlineData("223456789abcdef0,123456789abcdef0")]
  [InlineData("123456789abcdef0,")]
  public void Invalid_Perceptual_Evidence_Is_Rejected(string value)
    => Assert.False(PerceptualFingerprints.IsValid(PerceptualFingerprints.Mean8D8, PerceptualFingerprints.Mean8D8 + ":" + value));

  [Fact]
  public void Perceptual_Evidence_Has_Its_Own_Encoding_And_A_Defined_Bit_Distance()
  {
    var a = PerceptualFingerprints.Mean8D8 + ":123456789abcdef0";
    var b = PerceptualFingerprints.Mean8D8 + ":123456789abcdef3";
    Assert.True(PerceptualFingerprints.IsValid(PerceptualFingerprints.Mean8D8, a));
    Assert.False(ContentFingerprints.IsValid(PerceptualFingerprints.Mean8D8, a));
    Assert.Equal(2, PerceptualFingerprints.Distance(a, b));
    Assert.Equal(0, PerceptualFingerprints.Distance(a, a));
    Assert.Throws<ArgumentException>(() => PerceptualFingerprints.Distance(a, "xxh128:123456789abcdef0123456789abcdef0"));
    var managed = AnalysisEngineRegistry.ManagedOnly();
    Assert.Throws<AnalysisEngineUnavailableException>(() => managed.Snapshot(ManagedAnalysisEngine.EngineId,
      AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8));
  }

  private sealed record Label(string Name, string Family, string Variant, string Sha256);
  private sealed record Corpus(int Threshold, int TruePositives, int FalsePositives, int FalseNegatives, int TrueNegatives, Label[] Files);

  [NativeFact]
  public async Task Exact_Index_Scanner_Rejects_A_Supported_Perceptual_Engine_Before_Changing_The_Index()
  {
    using var tree = new TempTree();
    tree.Write("source/one.png", "unchanged source");
    var engine = new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable));
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), engine]);
    var snapshot = registry.Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    var index = tree.PathOf("data/index.db");
    using var store = IndexedWorkbenchStore.Open(index, startWorker: false, engines: registry);
    Assert.Empty(await store.AddSourcesAsync([tree.PathOf("source")], new SourceOptions(true, true, []), startScan: true));
    var database = IndexDatabase.OpenOrCreate(index);
    Assert.Throws<ArgumentException>(() => new SourceScanner(database, engine, snapshot));
    using var connection = database.Open();
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT SUM(analysis_generation) FROM jobs"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM analysis_results"));
    Assert.Equal("unchanged source", File.ReadAllText(tree.PathOf("source/one.png")));
  }

  [Fact]
  public void Similar_Groups_Require_Actual_Pair_Evidence_And_The_Declared_Distance()
  {
    var observation = new FileObservation(1, 1);
    var files = Enumerable.Range(1, 2).Select(id => new AnalysisInput(id, "images",
      Path.Combine(Path.GetTempPath(), $"image-{id}.png"), observation)).ToImmutableArray();
    var snapshot = new AnalysisEngineSnapshot("czkawka", NativeAnalysisEngine.Version, NativeAnalysisEngine.UpstreamCommit, 1, 1,
      AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8, "{}", "{}");
    var request = new AnalysisRequest("pairs", snapshot, files);
    var evidence = ImmutableArray.Create(new AnalysisFileEvidence(1, observation, AnalysisFileStatus.Succeeded,
      PerceptualFingerprints.Mean8D8 + ":123456789abcdef0", null), new AnalysisFileEvidence(2, observation, AnalysisFileStatus.Succeeded,
      PerceptualFingerprints.Mean8D8 + ":123456789abcdef3", null));
    var pair = new AnalysisCandidateGroup(AnalysisCapability.SimilarImages, [1, 2], 2, PerceptualFingerprints.DistanceDefinition);
    var batch = new AnalysisBatch("pairs", "czkawka", 1, evidence, [pair], new("Done", 2, 2, 0, null), true, true);
    var valid = new AnalysisResultValidator(request);
    valid.Accept(batch);
    valid.RequireFinal();
    Assert.Throws<InvalidDataException>(() => new AnalysisResultValidator(request).Accept(batch with { Groups = [pair with { Distance = 1 }] }));
    Assert.Throws<InvalidDataException>(() => new AnalysisResultValidator(request).Accept(batch with { Groups = [pair with { DistanceDefinition = "similarity percent" }] }));
    Assert.Throws<InvalidDataException>(() => new AnalysisResultValidator(request).Accept(batch with { Groups = [pair with { Kind = AnalysisCapability.ExactDuplicates }] }));
    Assert.Throws<InvalidDataException>(() => new AnalysisResultValidator(request).Accept(batch with { Files = [evidence[0]] }));
  }

  [NativeFact]
  public async Task Native_Image_Failure_Accounts_For_Each_Input_And_Is_Not_Complete_Success()
  {
    using var tree = new TempTree();
    var path = tree.Write("broken.png", "not a PNG");
    var engine = new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable));
    var snapshot = new AnalysisEngineRegistry([engine]).Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    var request = new AnalysisRequest("corrupt-image", snapshot, [new(1, "images", path, FileObservation.Capture(path))]);
    var validator = new AnalysisResultValidator(request);
    var evidence = new List<AnalysisFileEvidence>();
    await foreach (var batch in engine.AnalyzeAsync(request))
    {
      validator.Accept(batch);
      evidence.AddRange(batch.Files);
      if (batch.IsFinal) { Assert.False(batch.IsComplete); }
    }
    validator.RequireFinal();
    var failed = Assert.Single(evidence);
    Assert.Equal(AnalysisFileStatus.Failed, failed.Status);
    Assert.Null(failed.Fingerprint);
    Assert.NotEmpty(failed.Error!);
    Assert.Equal("not a PNG", File.ReadAllText(path));
  }

  [NativeFact]
  public async Task Real_Native_Labelled_Corpus_Measures_Misses_And_Does_Not_Promote_Similarity_To_Exact_Content()
  {
    using var tree = new TempTree();
    var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "similar-images");
    var corpus = JsonSerializer.Deserialize<Corpus>(await File.ReadAllTextAsync(Path.Combine(fixtures, "labels.json")), CorpusJson)!;
    foreach (var label in corpus.Files)
    {
      var bytes = await File.ReadAllBytesAsync(Path.Combine(fixtures, label.Name));
      Assert.Equal(label.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
      await File.WriteAllBytesAsync(tree.PathOf(label.Name), bytes);
    }
    var engine = new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable));
    var snapshot = new AnalysisEngineRegistry([engine]).Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    var files = corpus.Files.Select((label, i) => new AnalysisInput(i + 1, "images", tree.PathOf(label.Name), FileObservation.Capture(tree.PathOf(label.Name)))).ToImmutableArray();
    var request = new AnalysisRequest("labelled-images", snapshot, files);
    var validator = new AnalysisResultValidator(request);
    var evidence = new List<AnalysisFileEvidence>();
    await foreach (var batch in engine.AnalyzeAsync(request))
    {
      validator.Accept(batch);
      evidence.AddRange(batch.Files);
      Assert.Empty(batch.Groups); // Native emits per-file evidence; global pair review belongs to Core.
      if (batch.IsFinal) { Assert.True(batch.IsComplete); }
    }
    validator.RequireFinal();
    Assert.Equal(corpus.Files.Length, evidence.Count);
    Assert.All(evidence, file => Assert.Equal(AnalysisFileStatus.Succeeded, file.Status));
    var tp = 0; var fp = 0; var fn = 0; var tn = 0;
    for (var i = 0; i < corpus.Files.Length; i++)
    {
      for (var j = i + 1; j < corpus.Files.Length; j++)
      {
        var predicted = PerceptualFingerprints.Distance(evidence.Single(item => item.FileId == i + 1).Fingerprint!,
          evidence.Single(item => item.FileId == j + 1).Fingerprint!) <= corpus.Threshold;
        var labelled = corpus.Files[i].Family == corpus.Files[j].Family;
        if (predicted && labelled) { tp++; } else if (predicted) { fp++; } else if (labelled) { fn++; } else { tn++; }
      }
    }
    Assert.Equal((corpus.TruePositives, corpus.FalsePositives, corpus.FalseNegatives, corpus.TrueNegatives), (tp, fp, fn, tn));
    foreach (var label in corpus.Files)
    {
      Assert.Equal(label.Sha256, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(tree.PathOf(label.Name)))));
    }
  }
}
