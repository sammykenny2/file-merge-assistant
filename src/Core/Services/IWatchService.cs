namespace FileMergeAssistant.Services;

public interface IWatchService : IDisposable
{
  event EventHandler<FileChangeEventArgs> FileChanged;
  void Start(string path, bool recursive);
  void Stop();
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
