using System.Text;

namespace FileMergeAssistant.Tests.Scanning;

/// <summary>A throwaway folder tree under %TEMP% for scanner and store tests. Never points at real user folders.</summary>
internal sealed class TempTree : IDisposable
{
  public TempTree()
  {
    Root = Path.Combine(Path.GetTempPath(), "fma-tree-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(Root);
  }

  public string Root { get; }

  public string PathOf(string relative) => Path.Combine(Root, relative);

  /// <summary>Writes a file; the content decides the fingerprint, so equal text means equal content.</summary>
  public string Write(string relative, string content, DateTime? modified = null)
  {
    var path = PathOf(relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, Encoding.UTF8);
    File.SetLastWriteTimeUtc(path, modified ?? new DateTime(2024, 3, 11, 12, 0, 0, DateTimeKind.Utc));
    return path;
  }

  /// <summary>A small project: <paramref name="count"/> source files plus a README, all with shared content.</summary>
  public void Project(string folder, int count = 12)
  {
    Write(Path.Combine(folder, "README.md"), "# readme\n");
    Write(Path.Combine(folder, "package.json"), "{ \"name\": \"demo\" }\n");
    for (var i = 0; i < count; i++)
    {
      Write(Path.Combine(folder, "src", $"module{i:00}.ts"), $"export const value{i} = {i};\n" + new string('x', 40 + i));
    }
  }

  public void Dispose()
  {
    try
    {
      Directory.Delete(Root, recursive: true);
    }
    catch (IOException)
    {
      // Best effort; the temp folder is cleaned by the OS eventually.
    }
    catch (UnauthorizedAccessException)
    {
      // Same as above.
    }
  }
}
