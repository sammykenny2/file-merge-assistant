using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Localization;

namespace FileMergeAssistant.Gui.Tests;

/// <summary>
/// Runs WPF code on one long-lived STA thread with the application's resources loaded, the way the app does. A process
/// can only create one <see cref="Application"/>, so every GUI test shares this host.
/// </summary>
internal static class WpfHost
{
  private static readonly Lazy<Dispatcher> Host = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);
  private static readonly BindingErrorListener Listener = new();

  /// <summary>Keeps every default data path under a throwaway folder so no test writes to the real %LOCALAPPDATA%.</summary>
  [ModuleInitializer]
  internal static void Initialize()
  {
    Environment.SetEnvironmentVariable(AppDataPaths.DataDirectoryVariable,
      Path.Combine(Path.GetTempPath(), $"fma-gui-test-data-{Environment.ProcessId}"));
    // The screens the tests check are in Traditional Chinese, whatever language this Windows uses.
    AppText.Apply("zh-TW", []);
  }

  /// <summary>Runs an action on the WPF thread and returns the binding errors WPF reported while it ran.</summary>
  public static IReadOnlyList<string> Run(Action action)
  {
    var dispatcher = Host.Value;
    Exception? failure = null;
    List<string> errors = [];
    dispatcher.Invoke(() =>
    {
      Listener.Errors.Clear();
      try
      {
        action();
        Flush();
      }
      catch (Exception e)
      {
        failure = e;
      }
      errors = [.. Listener.Errors];
    });
    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
    return errors;
  }

  /// <summary>Waits for a task on the WPF thread while still processing its messages.</summary>
  public static void Wait(Task task)
  {
    var frame = new DispatcherFrame();
    task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
    Dispatcher.PushFrame(frame);
    task.GetAwaiter().GetResult();
  }

  /// <summary>Waits for a task on the WPF thread and returns its result.</summary>
  public static T Wait<T>(Task<T> task)
  {
    Wait((Task)task);
    return task.GetAwaiter().GetResult();
  }

  /// <summary>Lets pending layout, binding and loaded work finish.</summary>
  public static void Flush() =>
    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

  /// <summary>Lays an element out at a width with unlimited height and renders all of it.</summary>
  public static BitmapSource Render(FrameworkElement element, double width)
  {
    element.Measure(new Size(width, double.PositiveInfinity));
    element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
    element.UpdateLayout();
    Flush();
    element.Measure(new Size(width, double.PositiveInfinity));
    element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
    element.UpdateLayout();
    var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
    var bitmap = new RenderTargetBitmap((int)width, height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(element);
    return bitmap;
  }

  public static void Save(BitmapSource bitmap, string path)
  {
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(path);
    encoder.Save(stream);
  }

  private static Dispatcher Start()
  {
    Dispatcher? dispatcher = null;
    using var ready = new ManualResetEventSlim();
    var thread = new Thread(() =>
    {
      dispatcher = Dispatcher.CurrentDispatcher;
      SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
      var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
      app.Resources.Source = new Uri("/FileMergeAssistant;component/Resources/ApplicationResources.xaml", UriKind.Relative);
      PresentationTraceSources.Refresh();
      PresentationTraceSources.DataBindingSource.Listeners.Add(Listener);
      PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
      ready.Set();
      Dispatcher.Run();
    })
    {
      IsBackground = true,
      Name = "WPF test host",
    };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    ready.Wait();
    return dispatcher!;
  }

  private sealed class BindingErrorListener : TraceListener
  {
    public List<string> Errors { get; } = [];

    public override void Write(string? message)
    {
    }

    public override void WriteLine(string? message)
    {
      if (message is not null)
      {
        Errors.Add(message);
      }
    }
  }
}
