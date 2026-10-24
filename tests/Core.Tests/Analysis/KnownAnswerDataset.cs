using System.Text;
using System.Text.Json;
using FileWorkbench.Tests.Scanning;

namespace FileWorkbench.Tests.Analysis;

internal sealed class KnownAnswerDataset : IDisposable
{
  private readonly TempTree _tree = new();

  public KnownAnswerDataset()
  {
    using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "analysis", "known-answer.json")));
    foreach (var file in fixture.RootElement.GetProperty("files").EnumerateArray())
    {
      var relative = file.GetProperty("path").GetString()!;
      var path = _tree.PathOf(relative.Replace('/', Path.DirectorySeparatorChar));
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      var bytes = Encoding.UTF8.GetBytes(file.GetProperty("content").GetString()!);
      File.WriteAllBytes(path, bytes);
      Files.Add(new KnownAnswerFile(file.GetProperty("id").GetInt64(), path, bytes));
    }
    ExactGroups = fixture.RootElement.GetProperty("exactGroups").EnumerateArray()
      .Select(group => group.EnumerateArray().Select(id => id.GetInt64()).ToArray()).ToArray();
  }

  public string Root => _tree.Root;
  public List<KnownAnswerFile> Files { get; } = [];
  public long[][] ExactGroups { get; }

  public void Dispose() => _tree.Dispose();
}

internal sealed record KnownAnswerFile(long Id, string Path, byte[] Bytes);
