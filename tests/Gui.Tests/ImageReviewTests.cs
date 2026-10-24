using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileWorkbench.Analysis;
using FileWorkbench.Gui.ViewModels;
using FileWorkbench.Gui.Workbench;
using FileWorkbench.Index;
using FileWorkbench.Localization;
using FileWorkbench.Scanning;
using FileWorkbench.Sources;
using FileWorkbench.Workbench;
using Xunit;

namespace FileWorkbench.Gui.Tests;

[Collection(DisplayLanguageTests.Name)]
public sealed class ImageReviewTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "file-workbench-image-ui-" + Guid.NewGuid().ToString("N"));
  private sealed class Volumes : IVolumeInfoProvider
  {
    public VolumeInfo? Describe(string path) => new(Path.GetPathRoot(path)!, "FIXTURE", "fixture", VolumeKind.Fixed);
  }

  public void Dispose()
  {
    AppText.Apply("zh-TW", []);
    try { if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); } }
    catch (IOException) { }
  }

  [NativeGuiFact]
  public void Images_Are_Reviewed_And_Reopened_With_Missing_Native_Without_Changing_Main_Operations()
  {
    var source = Path.Combine(_root, "source");
    Directory.CreateDirectory(Path.Combine(source, "private"));
    var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "similar-images");
    File.Copy(Path.Combine(fixture, "a-original.png"), Path.Combine(source, "original.png"));
    File.Copy(Path.Combine(fixture, "a-resize.png"), Path.Combine(source, "resized.png"));
    File.Copy(Path.Combine(fixture, "a-original.png"), Path.Combine(source, "private", "excluded.png"));
    var originalBytes = File.ReadAllBytes(Path.Combine(source, "original.png"));
    var errors = WpfHost.Run(() =>
    {
      AppText.Apply("zh-TW", []);
      var index = Path.Combine(_root, "data", "index.db");
      var engines = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Environment.GetEnvironmentVariable("FWB_NATIVE_LIBRARY_DIR"))]);
      using var store = IndexedWorkbenchStore.Open(index, new Volumes(), startWorker: false, engines: engines);
      Assert.Empty(WpfHost.Wait(store.AddSourcesAsync([source], new SourceOptions(false, true, []), startScan: false)));
      using (var connection = IndexDatabase.OpenOrCreate(index).Open()) { IndexDatabase.Execute(connection, "UPDATE sources SET exclusions='private'"); }
      store.RefreshNow();
      var main = new MainViewModel(store, TestSettings.Create(Path.Combine(_root, "preferences")));
      main.ShowImages();
      var page = Assert.IsType<ImageReviewViewModel>(main.CurrentPage);
      Assert.True(main.IsImagesSelected);
      Assert.True(page.CanAnalyze);
      WpfHost.Wait(page.AnalyzeCommand.ExecuteAsync(null));
      WpfHost.Wait(page.PreviewTask);
      Assert.False(page.Busy);
      Assert.Single(page.Pairs);
      Assert.True(page.PreviewReady, page.PreviewError);
      Assert.NotNull(page.LeftPreview);
      Assert.NotNull(page.RightPreview);
      Assert.True(page.CanMark);
      Assert.Contains("分析完成", page.Status, StringComparison.Ordinal);
      WpfHost.Wait(page.MarkRelatedCommand.ExecuteAsync(null));
      Assert.Equal(ImageReviewChoice.Related, page.Pairs.Single().Choice);
      var path = page.ReportPath!;
      var archive = WpfHost.Wait(store.ImageReviews.LoadAsync(path));
      Assert.Equal(2, archive.Images.Length);
      Assert.Equal(ImageReviewChoice.Related, Assert.Single(archive.Decisions).Choice);
      Assert.Equal(["private"], Assert.Single(archive.Sources).Source.Exclusions);
      var shell = new ShellView { DataContext = main };
      var bitmap = WpfHost.Render(shell, 1440);
      if (Environment.GetEnvironmentVariable("FWB_SNAPSHOT_DIR") is { } snapshots)
      {
        Directory.CreateDirectory(snapshots);
        WpfHost.Save(bitmap, Path.Combine(snapshots, "image-review-zh.png"));
      }
      AppText.Apply("en", []);
      WpfHost.Flush();
      Assert.Contains("Analysis complete", page.Status, StringComparison.Ordinal);
      Assert.Equal("Related", page.Pairs.Single().Review);
      WpfHost.Render(shell, 1440);
      main.ShowSources();
      var missingEngines = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Path.Combine(_root, "missing-native"))]);
      using var missing = IndexedWorkbenchStore.Open(index, new Volumes(), startWorker: false, engines: missingEngines);
      var reopened = new MainViewModel(missing, TestSettings.Create(Path.Combine(_root, "preferences-2")));
      reopened.ShowImages();
      var review = Assert.IsType<ImageReviewViewModel>(reopened.CurrentPage);
      Assert.False(review.CanAnalyze);
      Assert.Contains("not installed", review.Availability, StringComparison.Ordinal);
      Assert.True(review.CanOpenSaved);
      WpfHost.Wait(review.OpenArchiveAsync(path));
      WpfHost.Wait(review.PreviewTask);
      Assert.True(review.PreviewReady, review.PreviewError);
      Assert.Equal(ImageReviewChoice.Related, review.Pairs.Single().Choice);
      var row = review.SelectedPair!;
      File.SetAttributes(row.First.Path, File.GetAttributes(row.First.Path) | FileAttributes.Offline);
      try
      {
        review.SelectedPair = null;
        review.SelectedPair = row;
        WpfHost.Wait(review.PreviewTask);
        Assert.False(review.PreviewReady);
        Assert.False(review.CanMark);
        Assert.NotNull(review.PreviewError);
      }
      finally { File.SetAttributes(row.First.Path, FileAttributes.Normal); }
      Assert.Empty(store.Plan); Assert.Empty(store.History); Assert.Empty(store.Jobs);
      Assert.Empty(missing.Plan); Assert.Empty(missing.History); Assert.Empty(missing.Jobs);
      using var final = IndexDatabase.OpenOrCreate(index).Open();
      Assert.Equal(0L, IndexDatabase.Scalar<long>(final, "SELECT COUNT(*) FROM files"));
      Assert.Equal(0L, IndexDatabase.Scalar<long>(final, "SELECT COUNT(*) FROM decisions"));
      reopened.ShowSources();
    });
    Assert.Empty(errors);
    Assert.Equal(originalBytes, File.ReadAllBytes(Path.Combine(source, "original.png")));
  }

  [NativeGuiTheory]
  [InlineData(1, 6000)]
  [InlineData(6000, 1)]
  [InlineData(64, 32)]
  public void Previews_Bound_Both_Axes_And_Never_Upscale_Small_Images(int width, int height)
  {
    var errors = WpfHost.Run(() =>
    {
      var source = Path.Combine(_root, "source");
      Directory.CreateDirectory(source);
      var pixels = new byte[width * height * 3];
      for (var y = 0; y < height; y++)
      {
        for (var x = 0; x < width; x++)
        {
          var value = (byte)((width >= height ? x : y) * 255 / (Math.Max(width, height) - 1));
          var offset = (y * width + x) * 3;
          pixels[offset] = value; pixels[offset + 1] = value; pixels[offset + 2] = value;
        }
      }
      var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
      var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
      var first = Path.Combine(source, "first.png");
      using (var output = File.Open(first, FileMode.CreateNew)) { encoder.Save(output); }
      File.Copy(first, Path.Combine(source, "second.png"));
      var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Environment.GetEnvironmentVariable("FWB_NATIVE_LIBRARY_DIR"))]);
      using var store = IndexedWorkbenchStore.Open(Path.Combine(_root, "data", "index.db"), new Volumes(), startWorker: false, engines: registry);
      Assert.Empty(WpfHost.Wait(store.AddSourcesAsync([source], new SourceOptions(false, true, []), startScan: false)));
      var main = new MainViewModel(store);
      main.ShowImages();
      var page = Assert.IsType<ImageReviewViewModel>(main.CurrentPage);
      WpfHost.Wait(page.AnalyzeCommand.ExecuteAsync(null));
      WpfHost.Wait(page.PreviewTask);
      Assert.Single(page.Pairs);
      Assert.True(page.PreviewReady, page.PreviewError);
      foreach (var preview in new[] { page.LeftPreview!, page.RightPreview! })
      {
        Assert.InRange(preview.PixelWidth, 1, Math.Min(700, width));
        Assert.InRange(preview.PixelHeight, 1, Math.Min(700, height));
      }
      main.ShowSources();
    });
    Assert.Empty(errors);
  }

  [NativeGuiFact]
  public void Leaving_During_Archive_Load_Cannot_Start_A_New_Preview_After_Disposal()
  {
    var source = Path.Combine(_root, "source");
    Directory.CreateDirectory(source);
    var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "similar-images");
    File.Copy(Path.Combine(fixture, "a-original.png"), Path.Combine(source, "original.png"));
    File.Copy(Path.Combine(fixture, "a-resize.png"), Path.Combine(source, "resized.png"));
    var errors = WpfHost.Run(() =>
    {
      var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Environment.GetEnvironmentVariable("FWB_NATIVE_LIBRARY_DIR"))]);
      using var store = IndexedWorkbenchStore.Open(Path.Combine(_root, "data", "index.db"), new Volumes(), startWorker: false, engines: registry);
      Assert.Empty(WpfHost.Wait(store.AddSourcesAsync([source], new SourceOptions(false, true, []), startScan: false)));
      var engine = registry.Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
      var report = WpfHost.Wait(store.AnalyzeImagesAsync(engine, 6));
      var path = WpfHost.Wait(store.ImageReviews.CreateAsync(report));
      // Valid trailing whitespace keeps load pending long enough to exercise the page transition.
      File.AppendAllText(path, new string(' ', 16 * 1024 * 1024));
      var main = new MainViewModel(store);
      main.ShowImages();
      var page = Assert.IsType<ImageReviewViewModel>(main.CurrentPage);
      var opening = page.OpenArchiveAsync(path);
      Assert.False(opening.IsCompleted);
      main.ShowSources();
      WpfHost.Wait(opening);
      Assert.Empty(page.Pairs);
      Assert.Null(page.LeftPreview); Assert.Null(page.RightPreview);
      Assert.True(page.PreviewTask.IsCompleted);
      Assert.False(page.CanOpenSaved); Assert.False(page.CanAnalyze);
      page.Dispose(); // Repeated disposal is safe.
    });
    Assert.Empty(errors);
  }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class NativeGuiTheoryAttribute : TheoryAttribute
{
  public NativeGuiTheoryAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FWB_NATIVE_LIBRARY_DIR")))
    { Skip = "Optional native GUI integration: run Build-NativeAnalysis.ps1 -Test."; }
  }
}
