using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileMergeAssistant.Analysis;

namespace FileMergeAssistant.AnalysisVerifier;

internal static class Program
{
  private sealed record Check(string Name, string Status, string Detail, bool Required = true);
  private sealed class SkippedCheckException(string message) : Exception(message);
  private static readonly List<Check> Checks = [];
  private static readonly string[] EngineIds = ["csharp", "czkawka"];
  private static readonly string[] ImageNames = ["a-original.png", "a-resize.png"];

  public static async Task<int> Main(string[] args)
  {
    if (args.Length % 2 != 0) { throw new ArgumentException("Use --work NEW_DIRECTORY --native TRUSTED_LIBRARY_DIRECTORY --fixtures FIXTURES."); }
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2) { options.Add(args[i], args[i + 1]); }
    var work = Path.GetFullPath(options["--work"]);
    PathText.RequireSupported(work);
    if (Directory.Exists(work) || File.Exists(work)) { throw new ArgumentException("Choose a new verification directory; existing content is never overwritten."); }
    Directory.CreateDirectory(work);
    var nativeDirectory = Path.GetFullPath(options["--native"]);
    var fixtures = Path.GetFullPath(options["--fixtures"]);
    var report = new Dictionary<string, object?>
    {
      ["formatVersion"] = 1, ["kind"] = "portable-analysis-contract", ["passed"] = false,
      ["platform"] = RuntimeInformation.OSDescription, ["rid"] = RuntimeInformation.RuntimeIdentifier,
      ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(), ["runtime"] = RuntimeInformation.FrameworkDescription,
      ["fileIdentityProvider"] = OperatingSystem.IsWindows() ? "Windows file ID and optional volume GUID" : "Metadata only; no physical identity provider yet",
      ["work"] = work, ["checks"] = Checks,
      ["supportScope"] = "Experimental Unicode-path read-only analysis only. WPF, persistent index, operations, restore and platform packaging are not verified by this tool.",
      ["limitations"] = new[] { "No arbitrary-byte POSIX filename enumeration.", "Non-Windows file observations currently use size/time without a physical identity provider.", "Cross-volume behavior needs a separate mounted-volume case; no external volume is assumed." },
    };
    try
    {
      var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(nativeDirectory)]);
      report["engines"] = registry.Descriptors;
      report["nativeSha256"] = File.Exists(Path.Combine(nativeDirectory, NativeAnalysisPaths.LibraryName))
        ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(nativeDirectory, NativeAnalysisPaths.LibraryName)))) : null;
      Require(registry.Descriptors.All(engine => engine.Availability == AnalysisAvailability.Available), "Both expected engines must be available; missing native is not a passing empty result.");
      var source = Path.Combine(work, "known-answer");
      var fixture = KnownAnswerFixture.Read(File.ReadAllText(Path.Combine(fixtures, "analysis", "known-answer.json")), source);
      Directory.CreateDirectory(source);
      var inputs = ImmutableArray.CreateBuilder<AnalysisInput>();
      var expectedBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
      foreach (var file in fixture.Files)
      {
        var path = file.Path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = file.Contents;
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { output.Write(bytes); }
        expectedBytes[path] = bytes;
        inputs.Add(new(file.Id, "fixture", path, FileObservation.Capture(path)));
      }
      var expectedGroups = fixture.Groups;
      foreach (var (id, algorithm) in new[] { ("csharp", ContentFingerprints.XxHash128), ("csharp", ContentFingerprints.Sha256), ("czkawka", ContentFingerprints.Blake3) })
      {
        await CheckAsync("known-answer-" + id + "-" + algorithm, async () =>
        {
          var files = await Analyze(registry, id, algorithm, inputs.ToImmutable()).ConfigureAwait(false);
          Require(files.All(item => item.Status == AnalysisFileStatus.Succeeded), "A known-answer file failed.");
          var groups = files.GroupBy(item => item.Fingerprint, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => string.Join(',', group.Select(file => file.FileId).Order())).Order(StringComparer.Ordinal);
          Require(groups.SequenceEqual(expectedGroups, StringComparer.Ordinal), "Exact groups differ from the independent fixture.");
          return "All seven files, both exact groups, correct algorithm and complete stream.";
        }).ConfigureAwait(false);
      }
      await CheckAsync("unicode-case-and-normalization", async () =>
      {
        var directory = Path.Combine(work, "unicode"); Directory.CreateDirectory(directory);
        var paths = new List<string>();
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string Write(string name, string content)
        {
          var path = Path.Combine(directory, name);
          var bytes = System.Text.Encoding.UTF8.GetBytes(content);
          File.WriteAllBytes(path, bytes);
          File.SetLastWriteTimeUtc(path, new DateTime(2024, 3, 11, 12, 0, 0, DateTimeKind.Utc));
          paths.Add(path); contents[path] = bytes; return path;
        }
        Write("Case.txt", "UPPER");
        var caseSensitive = !File.Exists(Path.Combine(directory, "case.txt"));
        if (caseSensitive) { Write("case.txt", "lower"); }
        Write("相片-\U0001f600-\ufffd.txt", "unicode");
        Write("é.txt", "composed");
        var normalizationInsensitive = File.Exists(Path.Combine(directory, "e\u0301.txt"));
        if (!normalizationInsensitive) { Write("e\u0301.txt", "decomposed"); }
        var files = paths.Select((path, i) => new AnalysisInput(i + 1, "unicode", path, FileObservation.Capture(path))).ToImmutableArray();
        var ascii = ImmutableArray.CreateBuilder<AnalysisInput>();
        for (var i = 0; i < paths.Count; i++)
        {
          var path = Path.Combine(work, $"ascii-reference-{i}.dat");
          File.WriteAllBytes(path, contents[paths[i]]);
          File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(paths[i]));
          ascii.Add(new(i + 1, "ascii", path, FileObservation.Capture(path)));
        }
        foreach (var engine in EngineIds)
        {
          var result = await Analyze(registry, engine, engine == "csharp" ? ContentFingerprints.Sha256 : ContentFingerprints.Blake3, files).ConfigureAwait(false);
          var references = await Analyze(registry, engine, engine == "csharp" ? ContentFingerprints.Sha256 : ContentFingerprints.Blake3, ascii.ToImmutable()).ConfigureAwait(false);
          Require(result.All(item => item.Status == AnalysisFileStatus.Succeeded), "Unicode path analysis failed.");
          foreach (var item in result)
          {
            Require(item.Fingerprint == references.Single(reference => reference.FileId == item.FileId).Fingerprint, "A Unicode/case path was substituted with another input.");
            if (engine == "csharp")
            {
              var bytes = contents[paths[checked((int)item.FileId - 1)]];
              Require(item.Fingerprint == ContentFingerprints.Prefix(ContentFingerprints.Sha256) + Convert.ToHexStringLower(SHA256.HashData(bytes)), "Unicode SHA-256 differs from known bytes.");
            }
          }
        }
        foreach (var (path, bytes) in contents) { Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "A Unicode/case source changed."); }
        return string.Create(CultureInfo.InvariantCulture, $"Observed caseSensitive={caseSensitive}, normalizationInsensitive={normalizationInsensitive}; {paths.Count} Unicode files analyzed.");
      }).ConfigureAwait(false);
      await CheckAsync("native-images", async () =>
      {
        var images = ImmutableArray.CreateBuilder<AnalysisInput>();
        foreach (var name in ImageNames)
        {
          var path = Path.Combine(work, name); File.Copy(Path.Combine(fixtures, "similar-images", name), path);
          images.Add(new(images.Count + 1, "images", path, FileObservation.Capture(path)));
        }
        var result = await Analyze(registry, "czkawka", PerceptualFingerprints.Mean8D8, images.ToImmutable(), AnalysisCapability.SimilarImages).ConfigureAwait(false);
        Require(result.All(item => item.Status == AnalysisFileStatus.Succeeded) && PerceptualFingerprints.Distance(result[0].Fingerprint!, result[1].Fingerprint!) == 0, "PNG resize known answer differs.");
        return "Distinct perceptual evidence; resize distance zero; no exact-content interpretation.";
      }).ConfigureAwait(false);
      await CheckAsync("source-symlink-refusal", async () =>
      {
        var target = Path.Combine(work, "link-target.txt"); File.WriteAllText(target, "link-target");
        var link = Path.Combine(work, "link.txt");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
          throw new SkippedCheckException("Symlink creation unavailable; no symlink support declared: " + error.Message);
        }
        var input = ImmutableArray.Create(new AnalysisInput(1, "s", link, FileObservation.Capture(link)));
        foreach (var engine in EngineIds)
        {
          var result = await Analyze(registry, engine, engine == "csharp" ? ContentFingerprints.Sha256 : ContentFingerprints.Blake3, input, requireComplete: false).ConfigureAwait(false);
          Require(result.Single().Status != AnalysisFileStatus.Succeeded, "The backend accepted a symlink input.");
        }
        Require(File.ReadAllText(target) == "link-target", "Symlink target changed.");
        return "Both engines refused the symlink; target unchanged.";
      }).ConfigureAwait(false);
      if (!OperatingSystem.IsWindows())
      {
        await CheckAsync("unix-permission-denial", async () =>
        {
          if (OperatingSystem.IsWindows()) { throw new SkippedCheckException("Unix modes are not supported on Windows."); }
          var path = Path.Combine(work, "permission.txt"); File.WriteAllText(path, "private");
          var input = new AnalysisInput(1, "permission", path, FileObservation.Capture(path));
          var original = File.GetUnixFileMode(path);
          try
          {
            File.SetUnixFileMode(path, UnixFileMode.None);
            var denied = false;
            try { using var readable = File.OpenRead(path); }
            catch (UnauthorizedAccessException) { denied = true; }
            Require(denied, "This account can bypass mode 000; rerun as an unprivileged user to verify denial.");
            foreach (var engine in EngineIds)
            {
              var result = await Analyze(registry, engine, engine == "csharp" ? ContentFingerprints.Sha256 : ContentFingerprints.Blake3, [input], requireComplete: false).ConfigureAwait(false);
              Require(result.Single().Status != AnalysisFileStatus.Succeeded, "Permission denial was reported as success.");
            }
          }
          finally { File.SetUnixFileMode(path, original); }
          return "Both engines report a failed/skipped file; original mode restored.";
        }).ConfigureAwait(false);
      }
      else { Checks.Add(new("unix-permission-denial", "NotApplicable", "Windows ACL testing is a separate acceptance case.", false)); }
      foreach (var (path, bytes) in expectedBytes) { Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "A known-answer source changed."); }
      Checks.Add(new("known-answer-source-contents", "Passed", "All original known-answer bytes unchanged."));
      var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).ToArray();
      Require(!loaded.Any(name => name is "FileMergeAssistant.Core" or "PresentationFramework" or "SixLabors.ImageSharp"), "Verifier unexpectedly loaded Windows workflow/UI/image dependencies.");
      Checks.Add(new("portable-dependency-boundary", "Passed", "No Windows Core, WPF or ImageSharp assemblies loaded."));
      report["passed"] = Checks.All(check => !check.Required || check.Status == "Passed");
      return (bool)report["passed"]! ? 0 : 2;
    }
    catch (Exception error) { report["error"] = error.ToString(); return 2; }
    finally
    {
      File.WriteAllText(Path.Combine(work, "report.json"), JsonSerializer.Serialize(report, AnalysisJson.Options));
      Console.WriteLine(JsonSerializer.Serialize(new { report = Path.Combine(work, "report.json"), passed = report["passed"] }, AnalysisJson.Options));
    }
  }

  private static async Task<List<AnalysisFileEvidence>> Analyze(AnalysisEngineRegistry registry, string id, string algorithm,
    ImmutableArray<AnalysisInput> files, AnalysisCapability capability = AnalysisCapability.ExactDuplicates, bool requireComplete = true)
  {
    var snapshot = registry.Snapshot(id, capability, algorithm);
    var request = new AnalysisRequest(Guid.NewGuid().ToString("N"), snapshot, files);
    var validator = new AnalysisResultValidator(request);
    var result = new List<AnalysisFileEvidence>(); var complete = false;
    await foreach (var batch in registry.Resolve(snapshot).AnalyzeAsync(request).ConfigureAwait(false))
    { validator.Accept(batch); result.AddRange(batch.Files); if (batch.IsFinal) { complete = batch.IsComplete; } }
    validator.RequireFinal();
    Require(result.Count == files.Length && (!requireComplete || complete), "Analysis coverage/final status is incomplete.");
    return result;
  }
  private static async Task CheckAsync(string name, Func<Task<string>> action)
  {
    try { Checks.Add(new(name, "Passed", await action().ConfigureAwait(false))); }
    catch (SkippedCheckException error) { Checks.Add(new(name, "Skipped", error.Message, false)); }
    catch (Exception error) { Checks.Add(new(name, "Failed", error.Message)); }
  }
  private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
