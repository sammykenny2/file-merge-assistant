using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileWorkbench.Analysis;
using FileWorkbench.Scanning;
using FileWorkbench.Tests.Scanning;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

public sealed class ImageReviewArchiveTests
{
  private static async Task<SimilarImageReport> Analyze(TempTree tree)
  {
    var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "similar-images");
    Directory.CreateDirectory(tree.PathOf("source"));
    File.Copy(Path.Combine(fixtures, "a-original.png"), tree.PathOf("source/original.png"));
    File.Copy(Path.Combine(fixtures, "a-resize.png"), tree.PathOf("source/resized.png"));
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable))]);
    var snapshot = registry.Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    return await new SimilarImageAnalysis(registry, tree.PathOf("data")).RunAsync([tree.PathOf("source")], snapshot);
  }

  [NativeFact]
  public async Task Archive_Saves_Only_Review_Metadata_And_Preserves_Choices_Without_Engine_Resolution()
  {
    using var tree = new TempTree();
    var report = await Analyze(tree);
    Assert.True(report.IsComplete, report.ToJson());
    var archive = new ImageReviewArchive(tree.PathOf("data"));
    var path = await archive.CreateAsync(report);
    var document = await archive.LoadAsync(path);
    Assert.Equal(report.Analysis.Engine, document.Engine);
    Assert.Equal(report.Analysis.IndexPath, document.IndexPath);
    Assert.Equal(2, document.Images.Length);
    Assert.Empty(document.Decisions);
    var pair = Assert.Single(document.Pairs);
    await archive.SaveDecisionsAsync(path, document.Revision, [new(pair.FirstId, pair.SecondId, ImageReviewChoice.Related)]);
    // Loading and reviewing is independent of registry/engine availability.
    var reopened = new ImageReviewArchive(tree.PathOf("data"));
    var saved = await reopened.LoadAsync(path);
    Assert.Equal(ImageReviewChoice.Related, Assert.Single(saved.Decisions).Choice);
    using var preview = reopened.OpenPreview(saved, pair.FirstId);
    using var memory = new MemoryStream();
    await preview.CopyToAsync(memory);
    Assert.Equal(await File.ReadAllBytesAsync(saved.Images.Single(file => file.Id == pair.FirstId).Path), memory.ToArray());
    await archive.SaveDecisionsAsync(path, saved.Revision, []);
    Assert.Empty((await archive.LoadAsync(path)).Decisions);
    Assert.Equal(2, Directory.GetFiles(tree.PathOf("source")).Length);
    Assert.Empty(Directory.GetFiles(archive.DirectoryPath, "*.partial"));
  }

  [NativeFact]
  public async Task Invalid_Choices_And_Outside_Archive_Paths_Cannot_Replace_A_Saved_Review()
  {
    using var tree = new TempTree();
    var report = await Analyze(tree);
    var archive = new ImageReviewArchive(tree.PathOf("data"));
    var path = await archive.CreateAsync(report);
    var original = await File.ReadAllBytesAsync(path);
    var revision = (await archive.LoadAsync(path)).Revision;
    await Assert.ThrowsAsync<InvalidDataException>(() => archive.SaveDecisionsAsync(path, revision, [new(999, 1000, ImageReviewChoice.Related)]));
    var pair = report.Pairs.Single();
    await Assert.ThrowsAsync<InvalidDataException>(() => archive.SaveDecisionsAsync(path, revision, [new(pair.FirstId, pair.SecondId, ImageReviewChoice.Pending)]));
    var foreign = tree.PathOf("source/review-foreign.json");
    await Assert.ThrowsAsync<ArgumentException>(() => archive.LoadAsync(foreign));
    Assert.False(File.Exists(foreign));
    Assert.Equal(original, await File.ReadAllBytesAsync(path));
    var bad = Path.Combine(archive.DirectoryPath, "review-invalid.json");
    await File.WriteAllTextAsync(bad, System.Text.Encoding.UTF8.GetString(original).Replace("\"jobs\":[{", "\"jobs\":[null,{", StringComparison.Ordinal));
    await Assert.ThrowsAsync<InvalidDataException>(() => archive.LoadAsync(bad));
    Assert.Empty(Directory.GetFiles(archive.DirectoryPath, "*.partial"));
  }

  [NativeFact]
  public async Task Preview_Rejects_Cloud_Only_Or_Replaced_File_With_Unchanged_Size_And_Time()
  {
    using var tree = new TempTree();
    var report = await Analyze(tree);
    var archive = new ImageReviewArchive(tree.PathOf("data"));
    var document = await archive.LoadAsync(await archive.CreateAsync(report));
    var file = document.Images[0];
    File.SetAttributes(file.Path, File.GetAttributes(file.Path) | FileAttributes.Offline);
    try { Assert.Throws<IOException>(() => archive.OpenPreview(document, file.Id)); }
    finally { File.SetAttributes(file.Path, FileAttributes.Normal); }
    var replacement = file.Path + ".replacement";
    await File.WriteAllBytesAsync(replacement, await File.ReadAllBytesAsync(file.Path));
    File.SetLastWriteTimeUtc(replacement, new DateTime(file.Observation.ModifiedUtcTicks, DateTimeKind.Utc));
    File.Move(replacement, file.Path, overwrite: true);
    Assert.Equal(file.Observation.Size, new FileInfo(file.Path).Length);
    Assert.Equal(file.Observation.ModifiedUtcTicks, File.GetLastWriteTimeUtc(file.Path).Ticks);
    Assert.Throws<IOException>(() => archive.OpenPreview(document, file.Id));
  }

  [NativeFact]
  public async Task A_Review_Directory_Junction_Into_A_Source_Is_Rejected_Before_Writing()
  {
    using var tree = new TempTree();
    var report = await Analyze(tree);
    var archive = new ImageReviewArchive(tree.PathOf("data"));
    var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"))
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
    start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
    start.ArgumentList.Add("New-Item -ItemType Junction -Path '" + archive.DirectoryPath.Replace("'", "''", StringComparison.Ordinal)
      + "' -Target '" + tree.PathOf("source").Replace("'", "''", StringComparison.Ordinal) + "' -ErrorAction Stop | Out-Null");
    using var process = Process.Start(start)!;
    await process.WaitForExitAsync();
    Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
    await Assert.ThrowsAsync<IOException>(() => archive.CreateAsync(report));
    Assert.Empty(Directory.GetFiles(tree.PathOf("source"), "review-*"));
    Assert.Empty(Directory.GetFiles(tree.PathOf("source"), "*.partial"));
  }

  [NativeFact]
  public async Task Archive_Preserves_All_Pair_Members_When_Cloud_Metadata_Exceeds_The_Detail_Cap()
  {
    using var tree = new TempTree();
    var report = await Analyze(tree);
    var local = report.Analysis.Files.Select((file, i) => file with { Id = 5000 + i }).ToImmutableArray();
    var cloud = Enumerable.Range(1, 2001).Select(id => new AnalysisSessionFile(id, local[0].SourceId,
      tree.PathOf($"source/cloud-{id}.png"), new FileObservation(1, 1), "CloudOnly", null));
    report = report with { Analysis = report.Analysis with { Files = cloud.Concat(local).ToImmutableArray() },
      Pairs = [new(local[0].Id, local[1].Id, report.Pairs.Single().Distance)] };
    var archive = new ImageReviewArchive(tree.PathOf("data"));
    var document = await archive.LoadAsync(await archive.CreateAsync(report));
    Assert.True(document.DetailsTruncated);
    Assert.Equal(2000, document.Images.Length);
    Assert.Contains(document.Images, file => file.Id == 5000);
    Assert.Contains(document.Images, file => file.Id == 5001);
    Assert.Single(document.Pairs);
  }

  [Theory]
  [InlineData("exclusions")]
  [InlineData("regenerableNames")]
  public async Task Malformed_Null_Rules_Are_Rejected_Before_Automatic_Preview(string property)
  {
    // A minimal owned archive need not load any native library to reject invalid source rules.
    using var tree = new TempTree();
    var archive = new ImageReviewArchive(tree.PathOf("data"));
    Directory.CreateDirectory(archive.DirectoryPath);
    var descriptor = new ManagedAnalysisEngine().Descriptor with { Id = "images", Capabilities = [new(AnalysisCapability.SimilarImages, [PerceptualFingerprints.Mean8D8], AnalysisMaturity.Experimental)] };
    var engine = new AnalysisEngineSnapshot("images", descriptor.Version, descriptor.UpstreamCommit, 1, 0, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8, "{}", "{}");
    var source = new ScanSourceSnapshot(new("s", tree.PathOf("source"), false, true, [], []), null, null, false);
    var document = new ImageReviewDocument(1, tree.PathOf("data/index.db"), descriptor, engine, "Completed", 6,
      PerceptualFingerprints.DistanceDefinition, [source], [], [], [], [], [], false, null);
    var node = JsonSerializer.SerializeToNode(document, AnalysisJson.Options)!;
    var rules = new JsonArray(); rules.Add((JsonNode?)null);
    node["sources"]![0]!["source"]![property] = rules;
    var path = Path.Combine(archive.DirectoryPath, "review-invalid-rules.json");
    await File.WriteAllTextAsync(path, node.ToJsonString());
    await Assert.ThrowsAsync<InvalidDataException>(() => archive.LoadAsync(path));
  }

  [NativeFact]
  public async Task Concurrent_Or_Replaced_Archive_Saves_Cannot_Apply_Stale_Choices()
  {
    using var tree = new TempTree();
    var report = await Analyze(tree);
    var archive = new ImageReviewArchive(tree.PathOf("data"));
    var path = await archive.CreateAsync(report);
    var original = await archive.LoadAsync(path);
    var pair = original.Pairs.Single();
    async Task<ImageReviewDocument?> Attempt(ImageReviewChoice choice)
    {
      try { return await archive.SaveDecisionsAsync(path, original.Revision, [new(pair.FirstId, pair.SecondId, choice)]); }
      catch (IOException) { return null; }
    }
    var results = await Task.WhenAll(Attempt(ImageReviewChoice.Related), Attempt(ImageReviewChoice.Different));
    var saved = Assert.Single(results.Where(result => result is not null))!;
    Assert.Single(results.Where(result => result is null));
    Assert.Equal(saved.Revision, (await archive.LoadAsync(path)).Revision);
    var changed = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
    changed["images"]![0]!["path"] = tree.PathOf("source/a-different-image.png");
    await File.WriteAllTextAsync(path, changed.ToJsonString());
    var replaced = await File.ReadAllBytesAsync(path);
    await Assert.ThrowsAsync<IOException>(() => archive.SaveDecisionsAsync(path, saved.Revision, []));
    Assert.Equal(replaced, await File.ReadAllBytesAsync(path));
  }
}
