using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FileWorkbench.Analysis;
using FileWorkbench.Index;
using FileWorkbench.Localization;
using FileWorkbench.Sources;
using FileWorkbench.Workbench;

namespace FileWorkbench.AcceptanceHost;

internal static class Program
{
  public static async Task<int> Main(string[] args)
  {
    var options = Parse(args);
    var root = Path.GetFullPath(options["--root"]);
    PathText.RequireSupported(root);
    Assert(root.IndexOfAny(['&', '|', '<', '>', '^', '%', '!', '"', '\r', '\n']) < 0,
      "Choose an acceptance output path without command-shell metacharacters for the owned link fixture.");
    Assert(!Directory.Exists(root) && !File.Exists(root), "The owned acceptance directory must be new.");
    AssertAncestors(root);
    var count = int.Parse(options["--count"], CultureInfo.InvariantCulture);
    Assert(count is >= 1000 and <= 300000, "Primary file count must be 1000..300000.");
    var engineId = options["--engine"];
    Assert(engineId is "csharp" or "czkawka", "Choose csharp or czkawka.");
    var fixtures = Path.GetFullPath(options["--fixtures"]);
    Directory.CreateDirectory(root);
    Environment.SetEnvironmentVariable("FWB_DATA_DIR", Path.Combine(root, "host-data"));
    AppText.Apply("en", []);
    var report = new Dictionary<string, object?>
    {
      ["formatVersion"] = 1, ["kind"] = "windows-synthetic-acceptance", ["passed"] = false,
      ["root"] = root, ["nominalPrimaryFiles"] = count, ["seed"] = Dataset.Seed,
      ["platform"] = RuntimeInformation.OSDescription, ["runtime"] = RuntimeInformation.FrameworkDescription,
      ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(), ["configuration"] = "Debug .NET; native Release when selected",
      ["cachePolicy"] = "Fresh File Workbench index, then same-index warm and +1% runs. OS filesystem cache is uncontrolled; generation and verification also warm it.",
      ["measurementScope"] = "Whole process including SQLite, workbench detection, oracle metadata and counters. Backend bytes are reported logical content bytes, not physical disk I/O.",
      ["guiInputLatency"] = "Not measured by this headless host; manual/interactive GUI acceptance remains pending.",
      ["physicalUsb"] = "Not tested. Offline case renames an owned directory on the same fixed volume.",
    };
    try
    {
      using (var marker = new FileStream(Path.Combine(root, "fixture.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
      {
        JsonSerializer.Serialize(marker, new { generator = "file-workbench-acceptance-v1", count, seed = Dataset.Seed, engineId }, AnalysisJson.Options);
      }
      var dataset = new Dataset(root, count, fixtures);
      var generate = Stopwatch.StartNew(); dataset.Generate();
      report["generationMilliseconds"] = generate.ElapsedMilliseconds;
      report["sources"] = dataset.Sources;
      report["longPathEntries"] = dataset.LongPathEntries;
      report["physicalRegularEntriesAtStart"] = dataset.PhysicalRegularEntries;
      report["fixtureContentBytesWritten"] = dataset.ContentBytes;
      report["driveFormat"] = new DriveInfo(Path.GetPathRoot(root)!).DriveFormat;
      var load = Stopwatch.StartNew();
      var measured = new MeteredEngine(engineId == "csharp" ? new ManagedAnalysisEngine() : new NativeAnalysisEngine(Path.GetFullPath(options["--native"])));
      Assert(measured.Descriptor.Availability == AnalysisAvailability.Available, measured.Descriptor.UnavailableReason ?? "Engine unavailable.");
      report["engineLoadMilliseconds"] = load.ElapsedMilliseconds;
      report["engine"] = measured.Descriptor;
      var registry = new AnalysisEngineRegistry(engineId == "csharp" ? [measured] : [new ManagedAnalysisEngine(), measured]);
      var selected = registry.Snapshot(engineId, AnalysisCapability.ExactDuplicates,
        engineId == "csharp" ? ContentFingerprints.XxHash128 : ContentFingerprints.Blake3);
      var index = Path.Combine(root, "index", "index.db");
      var setup = Stopwatch.StartNew();
      using var store = IndexedWorkbenchStore.Open(index, context: null, startWorker: false, engines: registry);
      report["indexSetupMilliseconds"] = setup.ElapsedMilliseconds;
      store.SelectNewJobEngine(selected);
      var phases = new List<object>(); report["phases"] = phases;
      phases.Add(await RunPhase("fresh-file-workbench-index", store, index, dataset, measured, async () =>
      {
        var problems = await store.AddSourcesAsync(dataset.Sources, new SourceOptions(true, true, dataset.Sources), startScan: true).ConfigureAwait(false);
        Assert(problems.Count == 0, string.Join("; ", problems));
        store.StartScanning();
      }).ConfigureAwait(false));
      var ids = SourceIds(index);
      phases.Add(await RunPhase("warm-same-index", store, index, dataset, measured, async () =>
      {
        foreach (var id in ids) { await store.ScanSourceAsync(id).ConfigureAwait(false); }
      }, expectedBackendFiles: 0).ConfigureAwait(false));
      dataset.AddOnePercent();
      phases.Add(await RunPhase("plus-one-percent", store, index, dataset, measured, async () =>
      {
        foreach (var id in ids) { await store.ScanSourceAsync(id).ConfigureAwait(false); }
      }, expectedBackendFiles: Math.Max(1, count / 100)).ConfigureAwait(false));
      report["simulatedOffline"] = await Offline(store, index, dataset, ids[^1]).ConfigureAwait(false);
      Assert(store.Plan.Count == 0 && store.History.Count == 0, "Acceptance analysis must not create an operation plan/history.");
      report["passed"] = true;
    }
    catch (Exception error) { report["error"] = error.ToString(); return 1; }
    finally
    {
      File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(report, AnalysisJson.Options));
      Console.WriteLine(JsonSerializer.Serialize(new { report = Path.Combine(root, "report.json"), passed = report["passed"] }, AnalysisJson.Options));
    }
    return 0;
  }

  private static async Task<object> RunPhase(string name, IndexedWorkbenchStore store, string index, Dataset dataset,
    MeteredEngine engine, Func<Task> queue, long? expectedBackendFiles = null)
  {
    var database = IndexDatabase.OpenOrCreate(index);
    using var beforeConnection = database.Open();
    var priorRow = IndexDatabase.Scalar<long>(beforeConnection, "SELECT COALESCE(MAX(rowid),0) FROM jobs");
    var filesBefore = engine.Files; var bytesBefore = engine.ReportedBytes;
    var before = ProcessSample.Capture();
    var clock = Stopwatch.StartNew();
    long firstStack = -1;
    void Changed(object? sender, WorkbenchChangedEventArgs e)
    {
      if (store.Stacks.Count > 0) { Interlocked.CompareExchange(ref firstStack, clock.ElapsedMilliseconds, -1); }
    }
    store.Changed += Changed;
    using var stop = new CancellationTokenSource();
    long peak = before.WorkingSet;
    var stages = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
    var sampler = Task.Run(async () =>
    {
      var last = clock.ElapsedMilliseconds;
      var previous = new Dictionary<string, string>(StringComparer.Ordinal);
      var heartbeat = 0L;
      while (!stop.IsCancellationRequested)
      {
        var now = clock.ElapsedMilliseconds;
        foreach (var phase in previous.Values) { stages.AddOrUpdate(phase, now - last, (_, value) => value + now - last); }
        previous.Clear(); last = now;
        peak = Math.Max(peak, ProcessSample.Capture().WorkingSet);
        using (var connection = database.Open())
        using (var command = IndexDatabase.Command(connection, "SELECT id,phase FROM jobs WHERE rowid>$row AND state IN ('Running','Pausing','Cancelling')", ("$row", priorRow)))
        using (var reader = command.ExecuteReader()) { while (reader.Read()) { previous[reader.GetString(0)] = reader.GetString(1); } }
        if (now - heartbeat >= 5000)
        {
          heartbeat = now;
          Console.WriteLine(JsonSerializer.Serialize(new { phase = name, elapsedMilliseconds = now, backendFiles = engine.Files - filesBefore, stacks = store.Stacks.Count }, AnalysisJson.Options));
        }
        try { await Task.Delay(100, stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
      }
    });
    long scanMilliseconds;
    long modelMilliseconds;
    ProcessSample after;
    try
    {
      await queue().ConfigureAwait(false);
      await store.WaitForScansAsync(TimeSpan.FromMinutes(30)).ConfigureAwait(false);
      scanMilliseconds = clock.ElapsedMilliseconds;
      using (var connection = database.Open())
      {
        Assert(IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM jobs WHERE rowid>$row", ("$row", priorRow)) == 6, "Expected six new jobs.");
        Assert(IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM jobs WHERE rowid>$row AND (state<>'Completed' OR analysis_complete<>1)", ("$row", priorRow)) == 0, "A job did not publish a complete result.");
      }
      var model = Stopwatch.StartNew(); store.RefreshNow(); modelMilliseconds = model.ElapsedMilliseconds;
      after = ProcessSample.Capture();
    }
    finally
    {
      store.Changed -= Changed;
      stop.Cancel(); await sampler.ConfigureAwait(false);
    }
    var backendFiles = engine.Files - filesBefore;
    if (expectedBackendFiles is { } expected) { Assert(backendFiles == expected, $"Unexpected backend inputs for {name}: {backendFiles}, expected {expected}."); }
    var verify = Stopwatch.StartNew();
    var groups = VerifyIndex(database, dataset);
    dataset.VerifyUnchanged();
    var sizes = new[] { index, index + "-wal", index + "-shm" }.Where(File.Exists).ToDictionary(path => Path.GetFileName(path)!, path => new FileInfo(path).Length, StringComparer.Ordinal);
    var result = new
    {
      name, scanMilliseconds, modelRefreshMilliseconds = modelMilliseconds, firstStackVisibleMilliseconds = firstStack < 0 ? (long?)null : firstStack,
      firstStackMeaning = name == "fresh-file-workbench-index" ? "First populated workbench model notification; no rendered GUI." : "Stacks may already be present from an earlier phase.",
      sampledStageMilliseconds = stages, sampledWorkingSetPeakBytes = Math.Max(peak, after.WorkingSet), workingSetBeforeBytes = before.WorkingSet,
      processReadBytes = after.ReadBytes - before.ReadBytes, processWriteBytes = after.WriteBytes - before.WriteBytes,
      processCpuMilliseconds = after.CpuMilliseconds - before.CpuMilliseconds,
      backendFiles, backendReportedContentBytes = engine.ReportedBytes - bytesBefore,
      files = dataset.ExpectedIndexed, groups, stacks = store.Stacks.Count, indexFiles = sizes,
      validationMilliseconds = verify.ElapsedMilliseconds, sourceContentsUnchanged = true,
    };
    Console.WriteLine(JsonSerializer.Serialize(new { completed = name, scanMilliseconds, backendFiles, result.files, groups }, AnalysisJson.Options));
    return result;
  }

  private static int VerifyIndex(IndexDatabase database, Dataset dataset)
  {
    using var connection = database.Open();
    Assert(IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files") == dataset.ExpectedIndexed, "Inventory count differs from the generator.");
    Assert(IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash IS NULL") == 0, "A generated candidate lacks exact evidence.");
    Assert(IndexDatabase.Scalar<long>(connection, "SELECT COALESCE(SUM(regenerable_files),0) FROM sources") == dataset.ExpectedRegenerable, "Regenerable counts differ.");
    Assert(IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE rel_path LIKE '%alias-to-source-0%' OR rel_path LIKE '%excluded.tmp'") == 0, "A link/excluded file entered inventory.");
    var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    using (var command = IndexDatabase.Command(connection, "SELECT f.hash,s.root,f.rel_path FROM files f JOIN sources s ON s.id=f.source_id WHERE f.hash IN (SELECT hash FROM files WHERE hash IS NOT NULL GROUP BY hash HAVING COUNT(*)>1)"))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        var hash = reader.GetString(0);
        if (!groups.TryGetValue(hash, out var paths)) { groups[hash] = paths = []; }
        paths.Add(Path.Combine(reader.GetString(1), reader.GetString(2)));
      }
    }
    Assert(groups.Values.Select(Dataset.Normalize).Order(StringComparer.Ordinal).SequenceEqual(dataset.ExpectedGroups, StringComparer.Ordinal), "Exact groups differ from the independent content-family oracle.");
    return groups.Count;
  }

  private static async Task<object> Offline(IndexedWorkbenchStore store, string index, Dataset dataset, string sourceId)
  {
    var source = dataset.Sources[^1]; var held = Path.Combine(dataset.Root, "temporarily-offline-source");
    MoveOwned(source, held, dataset.Root);
    try
    {
      await store.ScanSourceAsync(sourceId).ConfigureAwait(false);
      await store.WaitForScansAsync(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
      using (var connection = IndexDatabase.OpenOrCreate(index).Open())
      {
        Assert(IndexDatabase.Scalar<string>(connection, "SELECT state FROM jobs WHERE source_id=$id ORDER BY rowid DESC LIMIT 1", ("$id", sourceId)) == "WaitingForSource", "The offline source did not wait.");
        Assert(IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files") == dataset.ExpectedIndexed, "Offline inventory was lost.");
      }
    }
    finally { MoveOwned(held, source, dataset.Root); }
    await store.ScanSourceAsync(sourceId).ConfigureAwait(false);
    await store.WaitForScansAsync(TimeSpan.FromMinutes(30)).ConfigureAwait(false);
    var database = IndexDatabase.OpenOrCreate(index);
    using (var connection = database.Open())
    {
      Assert(IndexDatabase.Scalar<string>(connection, "SELECT state FROM jobs WHERE source_id=$id ORDER BY rowid DESC LIMIT 1", ("$id", sourceId)) == "Completed", "The restored source did not complete.");
    }
    VerifyIndex(database, dataset); dataset.VerifyUnchanged();
    return new { kind = "same-volume-directory-rename", waitedWithoutInventoryLoss = true, resumed = true };
  }

  private static string[] SourceIds(string index)
  {
    using var connection = IndexDatabase.OpenOrCreate(index).Open();
    using var command = IndexDatabase.Command(connection, "SELECT id FROM sources ORDER BY position");
    using var reader = command.ExecuteReader();
    var ids = new List<string>(); while (reader.Read()) { ids.Add(reader.GetString(0)); } return ids.ToArray();
  }
  private static void MoveOwned(string from, string to, string root)
  {
    Assert(Path.GetFullPath(from).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
      && Path.GetFullPath(to).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Move target escaped the owned fixture.");
    Assert((File.GetAttributes(from) & FileAttributes.ReparsePoint) == 0 && !Directory.Exists(to), "Invalid owned directory move.");
    Directory.Move(from, to);
  }
  private static void AssertAncestors(string path)
  {
    for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
    { if (parent.Exists) { Assert((parent.Attributes & FileAttributes.ReparsePoint) == 0, "Acceptance outputs must not traverse reparse points."); } }
  }
  private static Dictionary<string, string> Parse(string[] args)
  {
    Assert(args.Length % 2 == 0, "Use --root NEW_DIRECTORY --count N --engine csharp|czkawka --fixtures FIXTURES --native TRUSTED_LIBRARY_DIRECTORY.");
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2) { result.Add(args[i], args[i + 1]); }
    foreach (var name in new[] { "--root", "--count", "--engine", "--fixtures", "--native" }) { Assert(result.ContainsKey(name), "Missing argument: " + name); }
    return result;
  }
  private static void Assert(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
