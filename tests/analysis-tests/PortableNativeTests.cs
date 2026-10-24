using FileWorkbench.Analysis;
using FileWorkbench.Sources;
using FileWorkbench.Tests.Scanning;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

public sealed class PortableNativeTests
{
  [Fact]
  public void Optional_Identity_And_Lossless_Path_Text_Do_Not_Require_Windows_Core()
  {
    using var tree = new TempTree();
    var path = tree.Write("相片-\U0001f600-\ufffd.txt", "unicode");
    var observation = FileObservation.Capture(path);
    Assert.True(observation.Size > 0);
    Assert.Equal(OperatingSystem.IsWindows(), observation.Identity is not null);
    Assert.True(PathText.IsSupported(path));
    Assert.Throws<ArgumentException>(() => FileObservation.Capture(tree.PathOf(((char)0xd800) + ".txt")));
    if (observation.Identity is { } identity) { Assert.True(identity.SameAs(FileIdentityReader.Identify(path)!.Value)); }
  }

  [Fact]
  public void Missing_Native_Component_Is_An_Explicit_Unavailable_Capability()
  {
    using var tree = new TempTree();
    var engine = new NativeAnalysisEngine(tree.Root);
    Assert.Equal(AnalysisAvailability.MissingDependency, engine.Descriptor.Availability);
    Assert.Throws<AnalysisEngineUnavailableException>(() => new AnalysisEngineRegistry([engine]).Snapshot("czkawka",
      AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3));
  }

  [PortableNativeFact]
  public async Task Native_Exact_Analysis_Matches_The_Shared_Oracle_On_This_Runtime()
  {
    using var dataset = new KnownAnswerDataset();
    var engine = new NativeAnalysisEngine(Environment.GetEnvironmentVariable("FWB_NATIVE_LIBRARY_DIR"));
    Assert.Equal(AnalysisAvailability.Available, engine.Descriptor.Availability);
    var snapshot = new AnalysisEngineRegistry([engine]).Snapshot("czkawka", AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3);
    var request = ManagedAnalysisEngineTests.Request(dataset) with { Engine = snapshot };
    var validator = new AnalysisResultValidator(request);
    var evidence = new List<AnalysisFileEvidence>();
    await foreach (var batch in engine.AnalyzeAsync(request)) { validator.Accept(batch); evidence.AddRange(batch.Files); }
    validator.RequireFinal();
    Assert.Equal(dataset.Files.Count, evidence.Count);
    Assert.All(evidence, item => Assert.Equal(AnalysisFileStatus.Succeeded, item.Status));
    var groups = evidence.GroupBy(item => item.Fingerprint, StringComparer.Ordinal).Where(group => group.Count() > 1)
      .Select(group => string.Join(',', group.Select(item => item.FileId).Order())).Order(StringComparer.Ordinal);
    Assert.Equal(dataset.ExactGroups.Select(group => string.Join(',', group)).Order(StringComparer.Ordinal), groups);
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [PortableNativeFact]
  public async Task Native_Image_Evidence_Is_Separate_From_Exact_Content_Proof()
  {
    using var tree = new TempTree();
    var inputs = new List<AnalysisInput>();
    foreach (var name in new[] { "a-original.png", "a-resize.png" })
    {
      var path = tree.PathOf(name);
      File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "similar-images", name), path);
      inputs.Add(new(inputs.Count + 1, "s", path, FileObservation.Capture(path)));
    }
    var engine = new NativeAnalysisEngine(Environment.GetEnvironmentVariable("FWB_NATIVE_LIBRARY_DIR"));
    var snapshot = new AnalysisEngineRegistry([engine]).Snapshot("czkawka", AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    var request = new AnalysisRequest("portable-images", snapshot, [.. inputs]);
    var validator = new AnalysisResultValidator(request);
    var files = new List<AnalysisFileEvidence>();
    await foreach (var batch in engine.AnalyzeAsync(request)) { validator.Accept(batch); files.AddRange(batch.Files); Assert.Empty(batch.Groups); }
    validator.RequireFinal();
    Assert.Equal(2, files.Count);
    Assert.All(files, file => Assert.Equal(AnalysisFileStatus.Succeeded, file.Status));
    Assert.Equal(0, PerceptualFingerprints.Distance(files[0].Fingerprint!, files[1].Fingerprint!));
    Assert.False(ContentFingerprints.IsValid(ContentFingerprints.Sha256, files[0].Fingerprint!));
  }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class PortableNativeFactAttribute : FactAttribute
{
  public PortableNativeFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FWB_NATIVE_LIBRARY_DIR")))
    { Skip = "Set the absolute FWB_NATIVE_LIBRARY_DIR to run the optional native cases; skipped is not platform acceptance."; }
  }
}
