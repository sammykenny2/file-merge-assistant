namespace FileWorkbench.Services;

public interface IWatchService : IDisposable
{
  event EventHandler<FileChangeEventArgs> FileChanged;
  void StartWatching(string path, bool recursive);
  void StopWatching();
}

public class FileChangeEventArgs : EventArgs
{
  public required string FilePath { get; init; }
  public required WatchChangeType ChangeType { get; init; }
}

public enum WatchChangeType
{
  Created,
  Modified,
  Deleted,
  Renamed
}
