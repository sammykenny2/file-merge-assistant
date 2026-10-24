using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Automation;
using System.Windows.Input;
using FileMergeAssistant.Gui.ViewModels;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;
using ReviewStack = FileMergeAssistant.Stacks.Stack;

namespace FileMergeAssistant.Gui.Tests;

internal static class Program
{
  private static readonly List<object> Measurements = [];

  public static int Main(string[] args)
  {
    if (args.Length != 1) { throw new ArgumentException("Supply one NEW absolute report directory."); }
    var reportRoot = Path.GetFullPath(args[0]);
    if (Directory.Exists(reportRoot) || File.Exists(reportRoot)) { throw new IOException("Existing report directories are never overwritten."); }
    Directory.CreateDirectory(reportRoot);
    Environment.SetEnvironmentVariable(AppDataPaths.DataDirectoryVariable, Path.Combine(reportRoot, "data"));
    var report = new Dictionary<string, object?>
    {
      ["kind"] = "offscreen-wpf-scale-diagnostic", ["formatVersion"] = 1, ["passed"] = false,
      ["measurements"] = Measurements, ["viewport"] = "1440x900 at 96 dpi",
      ["os"] = RuntimeInformation.OSDescription, ["runtime"] = RuntimeInformation.FrameworkDescription,
      ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(), ["logicalProcessors"] = Environment.ProcessorCount,
      ["hostSha256"] = Digest(typeof(Program).Assembly.Location),
      ["coreSha256"] = Digest(typeof(IWorkbenchStore).Assembly.Location),
      ["analysisSha256"] = Digest(typeof(FileMergeAssistant.Analysis.AnalysisEngineRegistry).Assembly.Location),
      ["guiSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(ShellView).Assembly.Location))),
      ["limitations"] = "Synthetic in-memory demo-derived model; no real scan, native engine, disk index or visible window. Dispatcher queue delay is not physical input latency. Debug developer-host measurements are not release thresholds.",
    };
    try
    {
      var errors = WpfHost.Run(() => Run(reportRoot));
      report["bindingErrors"] = errors;
      if (errors.Count > 0) { throw new InvalidDataException("WPF binding errors occurred."); }
      report["passed"] = true;
      return 0;
    }
    catch (Exception error) { report["error"] = error.ToString(); return 2; }
    finally
    {
      File.WriteAllText(Path.Combine(reportRoot, "report.json"), JsonSerializer.Serialize(report));
      Console.WriteLine(JsonSerializer.Serialize(new { reportRoot, passed = report["passed"] }));
    }
  }

  private static void Run(string reportRoot)
  {
    var demo = new DemoWorkbenchStore();
    var folder = demo.Stacks.First(stack => stack.Kind == StackKind.Folder && stack.Rows.Count > 0);
    var file = demo.Stacks.First(stack => stack.Kind == StackKind.File);
    var stacks = Enumerable.Range(0, 10000).Select(i => (i % 2 == 0 ? folder : file) with
    { Id = "scale-" + i, Title = $"Synthetic stack {i:00000}", Status = StackStatus.Open, Block = StackBlock.None }).ToArray();
    var large = folder with { Id = "large-table", Title = "Synthetic 10000-row folder comparison",
      Rows = Enumerable.Range(0, 10000).Select(i => folder.Rows[0] with { RelativePath = $"synthetic/path-{i:00000}.txt" }).ToArray() };
    var store = DispatchProxy.Create<IWorkbenchStore, ScaleStore>();
    var proxy = (ScaleStore)(object)store;
    proxy.Inner = demo; proxy.Stacks = [.. stacks, large];
    MainViewModel? main = null;
    ShellView? shell = null;
    Measure("hub-10001-models-first-viewport", () =>
    {
      main = new MainViewModel(store);
      shell = new ShellView { DataContext = main };
      Render(shell);
    });
    var hub = (HubViewModel)main!.CurrentPage!;
    Record("hub-initial-visible-models", new { models = proxy.Stacks.Count, shown = hub.Groups.Sum(group => group.Items.Count) });
    Measure("hub-folder-filter-and-render", () => { hub.IsFolderKind = true; Render(shell!); });
    Measure("hub-sort-and-render", () => { hub.Sort = hub.SortOptions[^1]; Render(shell!); });
    Measure("hub-load-all-and-render", () =>
    {
      hub.IsAllKinds = true;
      var loads = 0;
      while (hub.HasMore && loads++ < 501) { hub.LoadMoreCommand.Execute(null); }
      if (hub.HasMore || hub.Groups.Sum(group => group.Items.Count) != 10001)
      { throw new InvalidDataException("Load-more did not retain all 10001 models."); }
      Render(shell!);
    });
    var hubLists = Descendants(shell!).OfType<ListBox>().ToArray();
    if (hubLists.Length != hub.Groups.Count || hubLists.Sum(list => list.Items.Count) != 10001)
    { throw new InvalidDataException("The full hub list lost a group or model."); }
    foreach (var list in hubLists)
    {
      var lastItem = (HubItemViewModel)list.Items[^1];
      list.ScrollIntoView(lastItem); Render(shell!);
      if (Descendants(list).OfType<VirtualizingStackPanel>().Single().Children.Count >= 100
        || list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1) is null)
      { throw new InvalidDataException("Hub rows are unbounded or the final row is inaccessible."); }
    }
    var lastHubItem = (HubItemViewModel)hubLists[^1].Items[^1];
    var open = Descendants(hubLists[^1]).OfType<Button>().Single(button => ReferenceEquals(button.Command, lastHubItem.Open));
    open.Command.Execute(open.CommandParameter);
    if (main.CurrentPage is not StackViewModel opened || opened.Title != lastHubItem.Title)
    { throw new InvalidDataException("A recycled hub button opened the wrong stack."); }
    Record("hub-full-list-and-recycled-command", new { models = 10001, groups = hubLists.Length, boundedContainers = true, finalRowOpened = true });
    // Restore the same component instance to exercise filtering after its full load-more state.
    main.CurrentPage = hub;
    Measure("hub-filter-after-load-more", () => { hub.IsFileKind = true; Render(shell!); });
    var filtered = hub.Groups.SelectMany(group => group.Items).ToArray();
    var kinds = proxy.Stacks.ToDictionary(stack => stack.Id, stack => stack.Kind, StringComparer.Ordinal);
    if (filtered.Length != 20 || filtered.Any(item => kinds[item.StackId] != StackKind.File))
    { throw new InvalidDataException("Filtering the fully loaded hub lost its page limit or returned the wrong kind."); }
    Measure("stack-10000-rows-first-viewport", () => { main.ShowStack(large.Id); Render(shell!); });
    var stackView = (StackViewModel)main.CurrentPage!;
    if (stackView.Rows.Count != StackViewModel.MaxRows || stackView.Conflicts.Count != 10000)
    { throw new InvalidDataException("The fixture must expose the 200-row summary and all 10000 conflicts."); }
    var table = Descendants(shell!).OfType<ListBox>().Single(control => ReferenceEquals(control.ItemsSource, stackView.Conflicts));
    var panel = Descendants(table).OfType<VirtualizingStackPanel>().Single();
    if (panel.Children.Count >= 100) { throw new InvalidDataException("Conflict controls are not bounded by the viewport."); }
    Record("table-containers", new { modelRows = 10000, comparisonRowsShown = stackView.Rows.Count, conflicts = table.Items.Count,
      firstRealized = table.ItemContainerGenerator.ContainerFromIndex(0) is not null,
      lastRealized = table.ItemContainerGenerator.ContainerFromIndex(9999) is not null });
    var scroll = Descendants(table).OfType<ScrollViewer>().FirstOrDefault(viewer => viewer.ScrollableHeight > 0)
      ?? Descendants(shell!).OfType<ScrollViewer>().Where(viewer => viewer.ScrollableHeight > 0).MaxBy(viewer => viewer.ScrollableHeight)
      ?? throw new InvalidDataException("No scrollable comparison viewport.");
    for (var i = 0; i < 12; i++)
    {
      var offset = scroll.ScrollableHeight * i / 11;
      Measure("table-scroll-" + i, () => { scroll.ScrollToVerticalOffset(offset); Render(shell!); });
    }
    var first = stackView.Conflicts[0]; var last = stackView.Conflicts[^1];
    void Choose(ConflictViewModel conflict, ConflictChoice choice)
    {
      table.ScrollIntoView(conflict); Render(shell!);
      var option = conflict.Options.Single(item => item.Choice == choice);
      var radio = Descendants(table).OfType<RadioButton>().Single(button => ReferenceEquals(button.DataContext, option));
      radio.IsChecked = true; WpfHost.Flush();
      if (!option.IsSelected) { throw new InvalidDataException("The radio did not update its bound conflict."); }
    }
    Choose(first, ConflictChoice.Both); Choose(last, ConflictChoice.Keeper);
    table.ScrollIntoView(first); Render(shell!);
    var firstOption = first.Options.Single(option => option.Choice == ConflictChoice.Both);
    if (!firstOption.IsSelected || !last.Options.Single(option => option.Choice == ConflictChoice.Keeper).IsSelected
      || !Descendants(table).OfType<RadioButton>().Single(button => ReferenceEquals(button.DataContext, firstOption)).IsChecked!.Value)
    { throw new InvalidDataException("Recycling lost or reassigned a conflict choice."); }
    if (Descendants(table).OfType<VirtualizingStackPanel>().Single().Children.Count >= 100)
    { throw new InvalidDataException("Recycling accumulated too many containers."); }
    Record("recycled-radio-choices", new { first = "Both", last = "Keeper", boundedContainers = true });
    var lists = Descendants(shell!).OfType<ListBox>().ToArray();
    if (lists.Length != 2) { throw new InvalidDataException("Both bounded review lists must be present."); }
    foreach (var list in lists)
    {
      if (!list.Focusable || string.IsNullOrWhiteSpace(AutomationProperties.GetName(list)))
      { throw new InvalidDataException("A bounded list needs a named keyboard focus entry."); }
      if (KeyboardNavigation.GetTabNavigation(list) != KeyboardNavigationMode.Continue
        || !Descendants(list).OfType<ListBoxItem>().All(item => item.Focusable))
      { throw new InvalidDataException("A bounded list prevents keyboard focus traversal."); }
      if (Descendants(list).OfType<VirtualizingStackPanel>().Single().Children.Count >= 100)
      { throw new InvalidDataException("A review list instantiated too many controls."); }
    }
    var outcomes = lists.Single(list => ReferenceEquals(list.ItemsSource, stackView.Outcome));
    if (outcomes.Items.Count != stackView.Outcome.Count || outcomes.Items.Count < 10000)
    { throw new InvalidDataException("Outcome models were hidden or truncated."); }
    Record("named-focus-entries", new { focusEntriesNamed = true, containersFocusable = true, outcomeModels = outcomes.Items.Count,
      outcomeContainers = Descendants(outcomes).OfType<VirtualizingStackPanel>().Single().Children.Count, physicalKeyboardVerified = false,
      detail = "Tab/arrow/PageDown focus behavior needs a visible disposable desktop; offscreen scrolling is tested separately." });
    WpfHost.Save(Render(shell!), Path.Combine(reportRoot, "comparison-viewport.png"));
    (main.CurrentPage as IDisposable)?.Dispose();
  }

  private static RenderTargetBitmap Render(FrameworkElement element)
  {
    element.Measure(new Size(1440, 900)); element.Arrange(new Rect(0, 0, 1440, 900));
    element.UpdateLayout(); WpfHost.Flush();
    var image = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32);
    image.Render(element);
    return image;
  }
  private static void Measure(string name, Action action)
  {
    Console.WriteLine(JsonSerializer.Serialize(new { starting = name }));
    var clock = Stopwatch.StartNew(); double? inputQueueDelay = null;
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => inputQueueDelay = clock.Elapsed.TotalMilliseconds));
    action(); WpfHost.Flush();
    using var process = Process.GetCurrentProcess(); process.Refresh();
    Record(name, new { elapsedMilliseconds = clock.Elapsed.TotalMilliseconds, inputQueueDelayMilliseconds = inputQueueDelay,
      workingSetBytes = process.WorkingSet64, processPeakWorkingSetBytes = process.PeakWorkingSet64 });
  }
  private static void Record(string name, object value)
  {
    var item = new { name, value }; Measurements.Add(item); Console.WriteLine(JsonSerializer.Serialize(item));
  }
  private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
  private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
  {
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
    {
      var child = VisualTreeHelper.GetChild(root, i); yield return child;
      foreach (var descendant in Descendants(child)) { yield return descendant; }
    }
  }
}

// This proxy permits model reads only; actions that could execute a plan are refused.
public class ScaleStore : DispatchProxy
{
  public IWorkbenchStore Inner { get; set; } = null!;
  public IReadOnlyList<ReviewStack> Stacks { get; set; } = [];
  protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
  {
    var method = targetMethod ?? throw new InvalidOperationException("Missing method.");
    if (method.Name == "get_Stacks") { return Stacks; }
    if (method.Name == nameof(IWorkbenchStore.WithDetailsAsync)) { return Task.FromResult((ReviewStack)args![0]!); }
    if (method.Name == nameof(IWorkbenchStore.DecisionFor)) { return null; }
    if (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name is "add_Changed" or "remove_Changed")
    { return method.Invoke(Inner, args); }
    throw new InvalidOperationException("Mutation is disabled in this read-only scale model: " + method.Name);
  }
}
