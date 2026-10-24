using System.Text;
using System.Text.Json;
using FileWorkbench.Analysis;

namespace FileWorkbench.AnalysisVerifier;

internal sealed record KnownAnswerFile(long Id, string Path, byte[] Contents);

internal static class KnownAnswerFixture
{
  // Validate the whole input before creating any fixture file.
  public static (KnownAnswerFile[] Files, string[] Groups) Read(string json, string source)
  {
    using var document = JsonDocument.Parse(json);
    var root = document.RootElement;
    if (root.GetProperty("version").GetInt32() != 1) { throw new InvalidDataException("Unsupported fixture version."); }
    var entries = root.GetProperty("files").EnumerateArray().ToArray();
    if (entries.Length != 7) { throw new InvalidDataException("The canonical fixture must contain seven files."); }
    var files = new List<KnownAnswerFile>();
    var ids = new HashSet<long>();
    var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    var prefix = Path.GetFullPath(source) + Path.DirectorySeparatorChar;
    foreach (var entry in entries)
    {
      var id = entry.GetProperty("id").GetInt64();
      var relative = entry.GetProperty("path").GetString();
      if (id <= 0 || !ids.Add(id) || string.IsNullOrWhiteSpace(relative)) { throw new InvalidDataException("Invalid fixture ID/path."); }
      PathText.RequireSupported(relative);
      var parts = relative.Replace('\\', '/').Split('/');
      if (Path.IsPathRooted(relative) || relative.Contains(':', StringComparison.Ordinal)
        || parts.Any(part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.')))
      { throw new InvalidDataException("Fixture paths must be plain relative descendants."); }
      var path = Path.GetFullPath(Path.Combine(source, Path.Combine(parts)));
      if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        || !paths.Add(path)) { throw new InvalidDataException("Fixture paths must be distinct owned descendants."); }
      var content = entry.GetProperty("content").GetString() ?? throw new InvalidDataException("Missing fixture content.");
      files.Add(new(id, path, Encoding.UTF8.GetBytes(content)));
    }
    var groups = root.GetProperty("exactGroups").EnumerateArray().Select(group =>
    {
      var members = group.EnumerateArray().Select(item => item.GetInt64()).Order().ToArray();
      if (members.Length < 2 || members.Distinct().Count() != members.Length || members.Any(id => !ids.Contains(id)))
      { throw new InvalidDataException("Invalid expected group."); }
      return string.Join(',', members);
    }).Order(StringComparer.Ordinal).ToArray();
    var actual = files.GroupBy(file => Convert.ToHexString(file.Contents), StringComparer.Ordinal)
      .Where(group => group.Count() > 1).Select(group => string.Join(',', group.Select(file => file.Id).Order())).Order(StringComparer.Ordinal);
    if (groups.Length != 2 || !actual.SequenceEqual(groups, StringComparer.Ordinal))
    { throw new InvalidDataException("Fixture groups must match the two canonical content groups."); }
    // Reject a file path that would also have to be another fixture's directory.
    foreach (var file in files)
    {
      var parent = Path.GetDirectoryName(file.Path);
      while (parent is not null && parent.Length >= prefix.Length)
      {
        if (paths.Contains(parent)) { throw new InvalidDataException("Fixture file/directory paths overlap."); }
        parent = Path.GetDirectoryName(parent);
      }
    }
    return (files.ToArray(), groups);
  }
}
