namespace FileWorkbench.Workbench;

/// <summary>
/// Counts the store operations that are moving or deleting the user's files right now: running the plan, retrying,
/// restoring and emptying recycle areas. Ending the app in the middle of one would leave the history behind the disk,
/// so exiting and installing an update wait until none is running (ADR-014).
/// </summary>
public static class FileChanges
{
  private static int _active;

  public static bool InProgress => Volatile.Read(ref _active) > 0;

  /// <summary>Marks an operation as running until the returned handle is disposed.</summary>
  public static IDisposable Begin()
  {
    Interlocked.Increment(ref _active);
    return new Handle();
  }

  private sealed class Handle : IDisposable
  {
    private int _ended;

    public void Dispose()
    {
      if (Interlocked.Exchange(ref _ended, 1) == 0)
      {
        Interlocked.Decrement(ref _active);
      }
    }
  }
}
