using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FileWorkbench.AcceptanceHost;

internal sealed class Dataset
{
  public const int Seed = 173;
  private readonly string _fixtureRoot;
  private readonly int _originalCount;
  private readonly Dictionary<string, List<string>> _groups = new(StringComparer.Ordinal);
  public Dataset(string root, int count, string fixtureRoot)
  {
    Root = root; _originalCount = count; _fixtureRoot = fixtureRoot;
    Sources = Enumerable.Range(0, 6).Select(i => Path.Combine(root, "sources", $"source-{i}")).ToArray();
  }
  public string Root { get; }
  public string[] Sources { get; }
  public int PrimaryCount { get; private set; }
  public int ExpectedIndexed => PrimaryCount + Sources.Length * 3 + 1; // Markers, real images and one hardlink.
  public int ExpectedRegenerable => Sources.Length * 100;
  public int PhysicalRegularEntries => ExpectedIndexed + ExpectedRegenerable + Sources.Length;
  public int LongPathEntries { get; private set; }
  public long ContentBytes { get; private set; }
  public string[] ExpectedGroups => _groups.Values.Where(paths => paths.Count > 1).Select(Normalize).Order(StringComparer.Ordinal).ToArray();

  public void Generate()
  {
    foreach (var source in Sources)
    {
      Directory.CreateDirectory(source);
      Write(Path.Combine(source, "project", "package.json"), "{}"u8.ToArray());
      Group("project-marker", Path.Combine(source, "project", "package.json"));
      for (var i = 0; i < 100; i++) { Write(Path.Combine(source, "project", "node_modules", $"generated-{i}.js"), "regenerable"u8.ToArray()); }
      Write(Path.Combine(source, "excluded.tmp"), "excluded"u8.ToArray());
      foreach (var name in new[] { "a-original.png", "a-resize.png" })
      {
        var path = Path.Combine(source, "photos", name);
        Write(path, File.ReadAllBytes(Path.Combine(_fixtureRoot, "similar-images", name)));
        Group(name, path);
      }
    }
    AddPrimary(_originalCount);
    var hardlink = Path.Combine(Sources[0], "hardlink.dat");
    RunLink("/H", hardlink, PrimaryPath(1));
    Group("primary-0", hardlink);
    // A reparse directory is deliberately outside the index; never follows it into another source.
    RunLink("/J", Path.Combine(Sources[1], "alias-to-source-0"), Sources[0]);
  }

  public void AddOnePercent() => AddPrimary(Math.Max(1, _originalCount / 100));

  private void AddPrimary(int count)
  {
    var end = checked(PrimaryCount + count);
    for (var i = PrimaryCount; i < end; i++)
    {
      var path = PrimaryPath(i);
      Write(path, PrimaryBytes(i));
      if (path.Length > 260) { LongPathEntries++; }
      if (i < DuplicateRange) { Group("primary-" + (i / 2).ToString(CultureInfo.InvariantCulture), path); }
    }
    PrimaryCount = end;
  }

  private int DuplicateRange => (_originalCount / 10) & ~1;
  private int ContentKey(int i) => i < DuplicateRange ? i / 2 : i;
  private byte[] PrimaryBytes(int i)
  {
    var key = ContentKey(i);
    var bytes = new byte[256 + key % 64 * 16];
    for (var j = 0; j < bytes.Length; j++) { bytes[j] = (byte)((key * 31L + j + Seed) % 251); }
    Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"File Workbench-fixture-v1 seed={Seed} key={key:D9}\n")).CopyTo(bytes, 0);
    return bytes;
  }
  private string PrimaryPath(int i)
  {
    var parent = Path.Combine(Sources[i % Sources.Length], $"collection-{i / 6000:D4}");
    if (i % 1000 == 0)
    {
      for (var depth = 0; depth < 6; depth++) { parent = Path.Combine(parent, "long-path-segment-" + new string('x', 22)); }
    }
    var name = i % 100 == 0 ? $"相片-\U0001f600-{i:D7}.dat" : $"file-{i:D7}.dat";
    return Path.Combine(parent, name);
  }
  private void Group(string key, string path)
  {
    if (!_groups.TryGetValue(key, out var paths)) { _groups[key] = paths = []; }
    paths.Add(path);
  }
  public static string Normalize(IEnumerable<string> paths) => string.Join('|', paths.Order(StringComparer.Ordinal));
  private void Write(string path, byte[] bytes)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); }
    File.SetLastWriteTimeUtc(path, new DateTime(2024, 3, 11, 12, 0, 0, DateTimeKind.Utc));
    ContentBytes += bytes.Length;
  }
  private static void RunLink(string mode, string path, string target)
  {
    using var process = Process.Start(new ProcessStartInfo
    {
      FileName = "cmd.exe", ArgumentList = { "/c", "mklink", mode, path, target },
      UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
    })!;
    process.WaitForExit();
    if (process.ExitCode != 0) { throw new IOException("Could not create the owned hardlink/junction fixture: " + process.StandardError.ReadToEnd()); }
  }

  public void VerifyUnchanged()
  {
    for (var i = 0; i < PrimaryCount; i++)
    {
      if (!File.ReadAllBytes(PrimaryPath(i)).AsSpan().SequenceEqual(PrimaryBytes(i))) { throw new InvalidDataException("A primary fixture changed."); }
    }
    foreach (var source in Sources)
    {
      if (File.ReadAllText(Path.Combine(source, "project", "package.json")) != "{}" || File.ReadAllText(Path.Combine(source, "excluded.tmp")) != "excluded") { throw new InvalidDataException("A fixture marker changed."); }
      for (var i = 0; i < 100; i++)
      {
        if (File.ReadAllText(Path.Combine(source, "project", "node_modules", $"generated-{i}.js")) != "regenerable") { throw new InvalidDataException("A regenerable fixture changed."); }
      }
      foreach (var name in new[] { "a-original.png", "a-resize.png" })
      {
        if (!File.ReadAllBytes(Path.Combine(source, "photos", name)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(_fixtureRoot, "similar-images", name)))) { throw new InvalidDataException("An image fixture changed."); }
      }
    }
    if (!File.ReadAllBytes(Path.Combine(Sources[0], "hardlink.dat")).AsSpan().SequenceEqual(PrimaryBytes(0))) { throw new InvalidDataException("The hardlink changed."); }
  }
}
