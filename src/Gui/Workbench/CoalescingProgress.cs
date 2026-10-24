using System.Windows.Threading;

namespace FileWorkbench.Gui.Workbench;

/// <summary>At most one queued UI update; a slow dispatcher retains only the newest progress value.</summary>
internal sealed class CoalescingProgress<T>(Action<T> apply) : IProgress<T>, IDisposable where T : class
{
  private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
  private readonly object _gate = new();
  private T? _latest;
  private bool _pending;
  private bool _disposed;

  public void Report(T value)
  {
    lock (_gate)
    {
      if (_disposed) { return; }
      _latest = value;
      if (_pending) { return; }
      _pending = true;
    }
    _dispatcher.BeginInvoke(() =>
    {
      T? current;
      lock (_gate)
      {
        _pending = false;
        if (_disposed) { return; }
        current = _latest;
        _latest = null;
      }
      if (current is not null) { apply(current); }
    }, DispatcherPriority.Background);
  }

  public void Dispose()
  {
    lock (_gate) { _disposed = true; _latest = null; }
  }
}
