using FileMergeAssistant.Analysis;
using FileMergeAssistant.Tests.Scanning;
using Xunit;
using System.Runtime.InteropServices;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class NativeAnalysisEngineTests
{
  private static NativeAnalysisEngine Native() => new(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable)!);

  [Fact]
  public void Missing_Or_Incompatible_Library_Is_Not_Reported_As_Empty_Success()
  {
    using var tree = new TempTree();
    var missing = new NativeAnalysisEngine(tree.Root);
    Assert.Equal(AnalysisAvailability.MissingDependency, missing.Descriptor.Availability);
    var registry = new AnalysisEngineRegistry([missing]);
    var error = Assert.Throws<AnalysisEngineUnavailableException>(() => registry.Snapshot(NativeAnalysisEngine.EngineId,
      AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3));
    Assert.Equal(AnalysisAvailability.MissingDependency, error.Availability);
    var name = OperatingSystem.IsWindows() ? "fma_czkawka_ffi.dll" : OperatingSystem.IsMacOS() ? "libfma_czkawka_ffi.dylib" : "libfma_czkawka_ffi.so";
    File.WriteAllText(Path.Combine(tree.Root, name), "not a native module");
    Assert.Equal(AnalysisAvailability.Incompatible, new NativeAnalysisEngine(tree.Root).Descriptor.Availability);
    Assert.Throws<ArgumentException>(() => new NativeAnalysisEngine("relative-native-directory"));
  }

  [NativeFact]
  public void A_Real_Native_Module_With_An_Incompatible_Abi_Is_Rejected_Before_Job_Creation()
  {
    var directory = Environment.GetEnvironmentVariable("FMA_WRONG_ABI_LIBRARY_DIR");
    Assert.False(string.IsNullOrWhiteSpace(directory), "Set FMA_WRONG_ABI_LIBRARY_DIR to the built native/test-libraries/wrong-abi output.");
    Assert.Equal(AnalysisAvailability.Incompatible, new NativeAnalysisEngine(directory).Descriptor.Availability);
  }

  [NativeFact]
  public async Task Cancellation_During_Native_Input_Upload_Is_Cancellation_And_Releases_The_Job()
  {
    var directory = Environment.GetEnvironmentVariable("FMA_CANCEL_UPLOAD_LIBRARY_DIR");
    Assert.False(string.IsNullOrWhiteSpace(directory), "Build the upload-cancel native test module and set FMA_CANCEL_UPLOAD_LIBRARY_DIR.");
    var name = OperatingSystem.IsWindows() ? "fma_czkawka_ffi.dll" : OperatingSystem.IsMacOS() ? "libfma_czkawka_ffi.dylib" : "libfma_czkawka_ffi.so";
    var module = NativeLibrary.Load(Path.Combine(directory!, name));
    try
    {
      var started = Marshal.GetDelegateForFunctionPointer<Probe>(NativeLibrary.GetExport(module, "fma_test_upload_started"));
      var destroyed = Marshal.GetDelegateForFunctionPointer<Probe>(NativeLibrary.GetExport(module, "fma_test_destroyed"));
      using var dataset = new KnownAnswerDataset();
      using var cancel = new CancellationTokenSource();
      var engine = new NativeAnalysisEngine(directory);
      var request = ManagedAnalysisEngineTests.Request(dataset) with { Engine = new AnalysisEngineRegistry([engine])
        .Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3) };
      await using var results = engine.AnalyzeAsync(request, cancel.Token).GetAsyncEnumerator();
      var pending = results.MoveNextAsync().AsTask();
      Assert.True(await Task.Run(() => SpinWait.SpinUntil(() => started() == 1, TimeSpan.FromSeconds(5))));
      cancel.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
      Assert.Equal(1u, destroyed());
    }
    finally { NativeLibrary.Free(module); }
  }

  [NativeFact]
  public async Task Loaded_Dll_Produces_Known_Answers_With_Core_Identity_Rechecks()
  {
    using var dataset = new KnownAnswerDataset();
    var engine = Native();
    Assert.Equal(AnalysisAvailability.Available, engine.Descriptor.Availability);
    var request = ManagedAnalysisEngineTests.Request(dataset) with
    {
      Engine = new AnalysisEngineRegistry([engine]).Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3),
    };
    var validator = new AnalysisResultValidator(request);
    var evidence = new List<AnalysisFileEvidence>();
    await foreach (var batch in engine.AnalyzeAsync(request))
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

  [NativeFact]
  public async Task Changed_Missing_Cancelled_And_Repeated_Jobs_Have_Controlled_Lifetimes()
  {
    using var dataset = new KnownAnswerDataset();
    var engine = Native();
    var snapshot = new AnalysisEngineRegistry([engine]).Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3);
    var request = ManagedAnalysisEngineTests.Request(dataset) with { Engine = snapshot };
    File.WriteAllText(dataset.Files[0].Path, "changed-after-request");
    File.Delete(dataset.Files[1].Path);
    var validator = new AnalysisResultValidator(request);
    var files = new List<AnalysisFileEvidence>();
    await foreach (var batch in engine.AnalyzeAsync(request))
    {
      validator.Accept(batch);
      files.AddRange(batch.Files);
      if (batch.IsFinal) { Assert.False(batch.IsComplete); }
    }
    validator.RequireFinal();
    Assert.Equal(AnalysisFileStatus.Changed, files.Single(file => file.FileId == 101).Status);
    Assert.Equal(AnalysisFileStatus.Failed, files.Single(file => file.FileId == 102).Status);

    using var healthy = new KnownAnswerDataset();
    var healthyRequest = ManagedAnalysisEngineTests.Request(healthy) with { Engine = snapshot };
    for (var iteration = 0; iteration < 75; iteration++)
    {
      using var cancel = new CancellationTokenSource();
      await using var result = engine.AnalyzeAsync(healthyRequest, cancel.Token).GetAsyncEnumerator();
      Assert.True(await result.MoveNextAsync());
      cancel.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result.MoveNextAsync());
    }
    await foreach (var batch in engine.AnalyzeAsync(healthyRequest))
    {
      if (batch.IsFinal) { Assert.True(batch.IsComplete); }
    }
  }
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate uint Probe();
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class NativeFactAttribute : FactAttribute
{
  public const string LibraryDirectoryVariable = "FMA_NATIVE_LIBRARY_DIR";
  public NativeFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LibraryDirectoryVariable)))
    {
      Skip = "Optional native integration: build the adapter and set FMA_NATIVE_LIBRARY_DIR to its absolute output directory.";
    }
  }
}
