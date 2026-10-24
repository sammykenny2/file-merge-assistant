using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using FileWorkbench.Analysis;
using FileWorkbench.Index;
using FileWorkbench.Plans;
using FileWorkbench.Scanning;
using FileWorkbench.Sources;
using FileWorkbench.Workbench;

namespace FileWorkbench.ScanHost;

/// <summary>Test-only process for termination and cold-start recovery. Never packaged with File Workbench.</summary>
internal static class Program
{
  public static async Task<int> Main(string[] args)
  {
    if (args.Length != 5) { throw new ArgumentException("Expected command, index, argument, argument, report path.", nameof(args)); }
    if (args[0] == "restore")
    {
      var native = new NativeAnalysisEngine(args[3]);
      if (native.Descriptor.Availability != AnalysisAvailability.MissingDependency)
      {
        throw new InvalidOperationException("The cold-start fixture must not have a native adapter installed.");
      }
      if (NativeModuleCount() != 0) { throw new InvalidOperationException("The cold-start host unexpectedly loaded an analysis native module."); }
      using var store = IndexedWorkbenchStore.Open(args[1], new Volumes(), context: null,
        engines: new AnalysisEngineRegistry([new ManagedAnalysisEngine(), native]));
      if (!store.History.Any(batch => batch.Id == args[2])) { throw new InvalidDataException("The historical batch is missing."); }
      await store.RestoreBatchAsync(args[2]);
      await store.WaitForScansAsync(TimeSpan.FromSeconds(30));
      var batch = store.History.Single(batch => batch.Id == args[2]);
      if (batch.Items.Any(item => item.State != BatchItemState.Restored)) { throw new InvalidDataException("The cold-start restore did not finish."); }
      var nativeModules = NativeModuleCount();
      if (nativeModules != 0) { throw new InvalidOperationException("Restore unexpectedly loaded an analysis native module."); }
      File.WriteAllText(args[4], JsonSerializer.Serialize(new
      {
        processId = Environment.ProcessId,
        nativeAvailability = native.Descriptor.Availability.ToString(),
        nativeModules,
        restored = batch.Items.Count,
      }));
      return 0;
    }
    if (args[0] != "interrupt") { throw new ArgumentException("Unknown test host command.", nameof(args)); }

    var engine = new InterruptEngine(args[2], args[3]);
    var database = IndexDatabase.OpenOrCreate(args[1]);
    using var worker = new ScanWorker(database, new Volumes(), [Path.GetDirectoryName(Path.GetFullPath(args[1]))!],
      new AnalysisEngineRegistry([new ManagedAnalysisEngine(), engine]));
    var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    worker.JobChanged += (_, finished) => { if (finished) { ended.TrySetResult(); } };
    worker.Start();
    worker.Wake();
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
    while (!engine.UploadStarted())
    {
      if (ended.Task.IsCompleted || DateTime.UtcNow > deadline) { throw new InvalidOperationException("The process did not enter the blocked native upload."); }
      await Task.Delay(5);
    }
    using (var connection = database.Open())
    {
      File.WriteAllText(args[4], JsonSerializer.Serialize(new
      {
        processId = Environment.ProcessId,
        staged = IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM analysis_results WHERE job_id IN (SELECT id FROM jobs WHERE state='Running')"),
      }));
    }
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
  }

  private static int NativeModuleCount()
  {
    using var current = Process.GetCurrentProcess();
    return current.Modules.Cast<ProcessModule>().Count(module => module.ModuleName.Contains("file_workbench_czkawka_ffi", StringComparison.OrdinalIgnoreCase));
  }

  private sealed class Volumes : IVolumeInfoProvider
  {
    public VolumeInfo? Describe(string path) => new(Path.GetPathRoot(path)!, "FIXTURE", "fixture", VolumeKind.Fixed);
  }

  private sealed class InterruptEngine : IFileAnalysisEngine
  {
    private readonly NativeAnalysisEngine _real;
    private readonly NativeAnalysisEngine _blocking;
    private readonly Probe _started;
    private int _requests;

    public InterruptEngine(string realDirectory, string blockingDirectory)
    {
      _real = new(realDirectory);
      _blocking = new(blockingDirectory);
      if (_real.Descriptor.Availability != AnalysisAvailability.Available || _blocking.Descriptor.Availability != AnalysisAvailability.Available)
      {
        throw new InvalidOperationException("Native process fixtures are unavailable.");
      }
      // Same lifetime policy as the real adapter: this module stays resident until the process exits.
      var module = NativeLibrary.Load(Path.Combine(blockingDirectory, "file_workbench_czkawka_ffi.dll"));
      _started = Marshal.GetDelegateForFunctionPointer<Probe>(NativeLibrary.GetExport(module, "fma_test_upload_started"));
    }

    public AnalysisEngineDescriptor Descriptor => _real.Descriptor;
    public bool UploadStarted() => _started() != 0;

    public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var selected = ++_requests == 2 ? _blocking : _real;
      await foreach (var batch in selected.AnalyzeAsync(request, cancellationToken)) { yield return batch; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Probe();
  }
}
