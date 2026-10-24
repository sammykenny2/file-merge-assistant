namespace FileWorkbench.Sources;

/// <summary>Source-root naming and physical observations; independent of scan/analysis implementations.</summary>
public interface ISourcePathService
{
  StringComparer Comparer { get; }
  bool DirectoryExists(string path);
  string Resolve(string path);
  FileIdentity? Identify(string path);
  bool Same(string first, string second);
  bool Inside(string inner, string outer);
  bool? SameOrInside(string inner, string outer);
}

/// <summary>Preserves the existing Windows path/alias rules. Other Core workflows remain Windows-specific.</summary>
public sealed class WindowsSourcePathService : ISourcePathService
{
  public static WindowsSourcePathService Instance { get; } = new();
  private WindowsSourcePathService() { }

  public StringComparer Comparer => StringComparer.OrdinalIgnoreCase;
  public bool DirectoryExists(string path) => Directory.Exists(path);
  public string Resolve(string path) => PathIdentity.Resolve(path);
  public FileIdentity? Identify(string path) => PathIdentity.Identify(path);
  public bool Same(string first, string second)
  {
    ArgumentNullException.ThrowIfNull(first); ArgumentNullException.ThrowIfNull(second);
    return Comparer.Equals(first.TrimEnd('\\'), second.TrimEnd('\\'));
  }
  public bool Inside(string inner, string outer)
  {
    ArgumentNullException.ThrowIfNull(inner); ArgumentNullException.ThrowIfNull(outer);
    var parent = outer.TrimEnd('\\') + "\\";
    return inner.Length > parent.Length - 1 && !Same(inner, outer) && inner.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
  }
  public bool? SameOrInside(string inner, string outer) => PathIdentity.SameOrInside(inner, outer);
}
