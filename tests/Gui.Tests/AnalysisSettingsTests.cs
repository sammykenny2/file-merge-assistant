using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Gui.Settings;
using FileMergeAssistant.Gui.ViewModels;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Workbench;
using FileMergeAssistant.Index;
using Xunit;

namespace FileMergeAssistant.Gui.Tests;

[Collection(DisplayLanguageTests.Name)]
public sealed class AnalysisSettingsTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "fma-analysis-ui-" + Guid.NewGuid().ToString("N"));

  public void Dispose()
  {
    AppText.Apply("zh-TW", []);
    try { if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); } }
    catch (IOException) { }
  }

  private sealed class Volumes : IVolumeInfoProvider
  {
    public VolumeInfo? Describe(string path) => new(Path.GetPathRoot(path)!, "FIXTURE", "fixture", VolumeKind.Fixed);
  }

  private static async Task WaitUntil(Func<bool> ready)
  {
    var clock = Stopwatch.StartNew();
    while (!ready())
    {
      if (clock.Elapsed > TimeSpan.FromSeconds(15)) { throw new TimeoutException("The settings operation did not finish."); }
      await Task.Delay(10);
    }
  }

  [Fact]
  public void Demo_Selection_Remains_On_The_UI_Thread_And_Switches_Without_Writing_An_Index()
  {
    var errors = WpfHost.Run(() =>
    {
      AppText.Apply("en", []);
      var store = new DemoWorkbenchStore();
      var main = new MainViewModel(store, TestSettings.Create(Path.Combine(_root, "preferences"), demo: true));
      main.ShowSettings();
      var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
      settings.SelectedAnalysis = settings.AnalysisChoices.Single(choice => choice.Snapshot.Algorithm == ContentFingerprints.Sha256);
      Assert.True(settings.AnalysisChoicesEnabled);
      Assert.Equal(ContentFingerprints.Sha256, store.NewJobEngine.Algorithm);
      WpfHost.Render(new ShellView { DataContext = main }, 1440);
    });
    Assert.Empty(errors);
  }

  [Fact]
  public void Selection_Is_Availability_Aware_Localized_And_Only_Affects_New_Jobs()
  {
    var source = Path.Combine(_root, "source");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "one.txt"), "same bytes");
    File.WriteAllText(Path.Combine(source, "two.txt"), "same bytes");
    var errors = WpfHost.Run(() =>
    {
      AppText.Apply("en", []);
      var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Path.Combine(_root, "missing-native"))]);
      using var store = IndexedWorkbenchStore.Open(Path.Combine(_root, "data", "index.db"), new Volumes(), context: null, startWorker: false, engines: registry);
      Assert.Empty(WpfHost.Wait(store.AddSourcesAsync([source], new SourceOptions(true, true, []), startScan: true)));
      var originalJob = store.Jobs.Single().Snapshot!.Serialize();
      var main = new MainViewModel(store, TestSettings.Create(Path.Combine(_root, "preferences")));
      main.ShowSettings();
      var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
      Assert.Contains("Experimental", settings.AnalysisDetail, StringComparison.Ordinal);
      Assert.Contains("not installed", settings.AnalysisDiagnostics, StringComparison.Ordinal);
      Assert.False(settings.AnalysisChoices.Single(choice => choice.Snapshot.EngineId == NativeAnalysisEngine.EngineId).Enabled);
      settings.SelectedAnalysis = settings.AnalysisChoices.Single(choice => choice.Snapshot.Algorithm == ContentFingerprints.Sha256);
      WpfHost.Wait(WaitUntil(() => settings.AnalysisChoicesEnabled && store.NewJobEngine.Algorithm == ContentFingerprints.Sha256));
      Assert.Equal(originalJob, store.Jobs.Single().Snapshot!.Serialize());
      AppText.Apply("zh-TW", []);
      WpfHost.Flush();
      Assert.Contains("實驗性", settings.AnalysisDetail, StringComparison.Ordinal);
      Assert.Contains("尚未安裝", settings.AnalysisDiagnostics, StringComparison.Ordinal);
      var shell = new ShellView { DataContext = main };
      var bitmap = WpfHost.Render(shell, 1440);
      if (Environment.GetEnvironmentVariable("FMA_SNAPSHOT_DIR") is { } snapshots)
      {
        Directory.CreateDirectory(snapshots);
        WpfHost.Save(bitmap, Path.Combine(snapshots, "analysis-settings-zh.png"));
      }
      main.ShowSources();
      var sources = Assert.IsType<SourcesViewModel>(main.CurrentPage);
      Assert.Contains(ContentFingerprints.XxHash128, Assert.Single(sources.Waiting).Analysis, StringComparison.Ordinal);
      Assert.Contains(source, sources.Waiting.Single().AnalysisDetails, StringComparison.Ordinal);
      store.StartScanning();
      WpfHost.Wait(store.WaitForScansAsync(TimeSpan.FromSeconds(30)));
      store.RefreshNow();
      Assert.Contains(ContentFingerprints.XxHash128, Assert.Single(sources.Finished).Analysis, StringComparison.Ordinal);
      Assert.Equal(Tone.Ok, sources.Finished.Single().State.Tone);
      WpfHost.Render(shell, 1440);
    });
    Assert.Empty(errors);
  }

  [NativeGuiFact]
  public void Comparison_Runs_Separately_Saves_The_Report_And_Leaves_The_Main_Plan_And_Queue_Alone()
  {
    var source = Path.Combine(_root, "source");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "one.txt"), "same bytes");
    File.WriteAllText(Path.Combine(source, "two.txt"), "same bytes");
    var errors = WpfHost.Run(() =>
    {
      AppText.Apply("en", []);
      var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Environment.GetEnvironmentVariable("FMA_NATIVE_LIBRARY_DIR"))]);
      var index = Path.Combine(_root, "data", "index.db");
      using var store = IndexedWorkbenchStore.Open(index, new Volumes(), context: null, engines: registry);
      Assert.Empty(WpfHost.Wait(store.AddSourcesAsync([source], new SourceOptions(true, true, []), startScan: true)));
      WpfHost.Wait(store.WaitForScansAsync(TimeSpan.FromSeconds(30)));
      store.RefreshNow();
      var jobId = store.Jobs.Single().Id;
      var main = new MainViewModel(store, TestSettings.Create(Path.Combine(_root, "preferences")));
      main.ShowSettings();
      var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
      Assert.True(settings.CanCompareEngines);
      WpfHost.Wait(settings.CompareEnginesCommand.ExecuteAsync(null));
      Assert.False(settings.ComparisonBusy);
      Assert.True(settings.HasComparisonReport);
      Assert.True(File.Exists(settings.ComparisonReportPath));
      Assert.Contains("agree", settings.ComparisonStatus, StringComparison.Ordinal);
      using var report = JsonDocument.Parse(File.ReadAllText(settings.ComparisonReportPath!));
      Assert.True(report.RootElement.GetProperty("groupsAgree").GetBoolean());
      var firstIndex = report.RootElement.GetProperty("first").GetProperty("indexPath").GetString();
      var secondIndex = report.RootElement.GetProperty("second").GetProperty("indexPath").GetString();
      Assert.NotEqual(firstIndex, secondIndex);
      Assert.NotEqual(index, firstIndex);
      Assert.Equal(jobId, store.Jobs.Single().Id);
      Assert.Empty(store.Plan);
      Assert.Empty(store.History);
      AppText.Apply("zh-TW", []);
      WpfHost.Flush();
      Assert.Contains("一致", settings.ComparisonStatus, StringComparison.Ordinal);
      var bitmap = WpfHost.Render(new ShellView { DataContext = main }, 1440);
      if (Environment.GetEnvironmentVariable("FMA_SNAPSHOT_DIR") is { } snapshots)
      {
        Directory.CreateDirectory(snapshots);
        WpfHost.Save(bitmap, Path.Combine(snapshots, "analysis-comparison-zh.png"));
      }
      Assert.All(Directory.GetFiles(source), file => Assert.Equal("same bytes", File.ReadAllText(file)));
    });
    Assert.Empty(errors);
  }

  [NativeGuiFact]
  public void Selecting_The_Installed_Native_Engine_Creates_A_New_Frozen_Job_And_Real_Results()
  {
    var source = Path.Combine(_root, "source");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "one.txt"), "same bytes");
    File.WriteAllText(Path.Combine(source, "two.txt"), "same bytes");
    var errors = WpfHost.Run(() =>
    {
      AppText.Apply("en", []);
      var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Environment.GetEnvironmentVariable("FMA_NATIVE_LIBRARY_DIR"))]);
      var index = Path.Combine(_root, "data", "index.db");
      using var store = IndexedWorkbenchStore.Open(index, new Volumes(), context: null, startWorker: false, engines: registry);
      Assert.Empty(WpfHost.Wait(store.AddSourcesAsync([source], new SourceOptions(true, true, []), startScan: true)));
      var original = store.Jobs.Single();
      var main = new MainViewModel(store, TestSettings.Create(Path.Combine(_root, "preferences")));
      main.ShowSettings();
      var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
      var native = settings.AnalysisChoices.Single(choice => choice.Snapshot.EngineId == NativeAnalysisEngine.EngineId);
      Assert.True(native.Enabled);
      settings.SelectedAnalysis = native;
      WpfHost.Wait(WaitUntil(() => settings.AnalysisChoicesEnabled && store.NewJobEngine.EngineId == NativeAnalysisEngine.EngineId));
      Assert.Equal(ManagedAnalysisEngine.EngineId, store.Jobs.Single().Snapshot!.Engine.EngineId);
      WpfHost.Wait(store.RestartJobAsync(original.Id));
      var queued = store.Jobs.Single(job => job.State == ScanJobState.Queued);
      Assert.NotEqual(original.Id, queued.Id);
      Assert.Equal(NativeAnalysisEngine.UpstreamCommit, queued.Snapshot!.Engine.UpstreamCommit);
      Assert.Equal(ContentFingerprints.Blake3, queued.Snapshot.Engine.Algorithm);
      store.StartScanning();
      WpfHost.Wait(store.WaitForScansAsync(TimeSpan.FromSeconds(30)));
      store.RefreshNow();
      main.ShowSources();
      var sources = Assert.IsType<SourcesViewModel>(main.CurrentPage);
      var finished = sources.Finished.Single(job => job.Analysis.Contains("Czkawka", StringComparison.Ordinal));
      Assert.Equal(Tone.Ok, finished.State.Tone);
      Assert.Contains(NativeAnalysisEngine.UpstreamCommit, finished.AnalysisDetails, StringComparison.Ordinal);
      using var connection = IndexDatabase.OpenOrCreate(index).Open();
      Assert.Equal(2L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash LIKE 'blake3-v1:%'"));
      WpfHost.Render(new ShellView { DataContext = main }, 1440);
    });
    Assert.Empty(errors);
  }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class NativeGuiFactAttribute : FactAttribute
{
  public NativeGuiFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FMA_NATIVE_LIBRARY_DIR")))
    {
      Skip = "Optional native GUI integration: run Build-NativeAnalysis.ps1 -Test.";
    }
  }
}
