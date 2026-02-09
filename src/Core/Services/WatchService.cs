using Serilog;

namespace FileMergeAssistant.Services;

public class WatchService : IWatchService
{
  private FileSystemWatcher? _watcher;
  private readonly ILogger _logger;

  public event EventHandler<FileChangeEventArgs>? FileChanged;

  public WatchService(ILogger logger)
  {
    _logger = logger;
  }

  public void Start(string path, bool recursive)
  {
    Stop();

    _watcher = new FileSystemWatcher(path)
    {
      IncludeSubdirectories = recursive,
      NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
      EnableRaisingEvents = true
    };

    _watcher.Created += (_, e) => OnFileChanged(e.FullPath, WatchChangeType.Created);
    _watcher.Changed += (_, e) => OnFileChanged(e.FullPath, WatchChangeType.Modified);
    _watcher.Deleted += (_, e) => OnFileChanged(e.FullPath, WatchChangeType.Deleted);
    _watcher.Renamed += (_, e) => OnFileChanged(e.FullPath, WatchChangeType.Renamed);
    _watcher.Error += (_, e) => _logger.Warning(e.GetException(), "File watcher error");

    _logger.Information("Watch mode started for {Path} (recursive: {Recursive})", path, recursive);
  }

  public void Stop()
  {
    if (_watcher != null)
    {
      _watcher.EnableRaisingEvents = false;
      _watcher.Dispose();
      _watcher = null;
      _logger.Information("Watch mode stopped");
    }
  }

  private void OnFileChanged(string filePath, WatchChangeType changeType)
  {
    _logger.Debug("File {ChangeType}: {FilePath}", changeType, filePath);
    FileChanged?.Invoke(this, new FileChangeEventArgs
    {
      FilePath = filePath,
      ChangeType = changeType
    });
  }

  public void Dispose()
  {
    Stop();
    GC.SuppressFinalize(this);
  }
}
