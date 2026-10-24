using System.Text;
using FileWorkbench.Updates;
using Xunit;

namespace FileWorkbench.Tests.Updates;

public class UpdateFeedTests
{
  private const string Repo = "owner/app";
  private static readonly Version Current = new(0, 1, 0);

  internal static byte[] Feed(params string[] tagLinks)
  {
    var entries = string.Concat(tagLinks.Select(link =>
      $"<entry><id>{link}</id><link rel=\"alternate\" type=\"text/html\" href=\"{link}\"/><title>t</title></entry>"));
    return Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><feed xmlns=\"http://www.w3.org/2005/Atom\">{entries}</feed>");
  }

  internal static string Page(string tag, string repo = Repo) => $"https://github.com/{repo}/releases/tag/{tag}";

  internal static byte[] Manifest(string version, string tag, string name = "FileWorkbench-0.2.0-x64-setup.exe",
    long size = 1234, string sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", string platform = "windows", string summary = "Fixes")
  {
    var json = $$"""
      {"schemaVersion":1,"platform":"{{platform}}","version":"{{version}}","tag":"{{tag}}","summary":"{{summary}}",
       "assets":[{"arch":"x64","name":"{{name}}","size":{{size}},"sha256":"{{sha256}}"}]}
      """;
    return Encoding.UTF8.GetBytes(json);
  }

  [Theory]
  [InlineData("1.2.3", true)]
  [InlineData("0.0.0", true)]
  [InlineData("01.2.3", false)]
  [InlineData("1.2", false)]
  [InlineData("1.2.3-beta", false)]
  [InlineData("1.2.3 ", false)]
  [InlineData(null, false)]
  public void ParseVersion_Is_Strict(string? text, bool valid)
  {
    Assert.Equal(valid, UpdateFeed.ParseVersion(text) is not null);
  }

  [Theory]
  [InlineData("sammykenny2/messy-file-workbench", true)]
  [InlineData("owner/app", true)]
  [InlineData("owner", false)]
  [InlineData("owner/app/extra", false)]
  [InlineData("https://github.com/owner/app", false)]
  [InlineData("", false)]
  public void IsValidRepository_Accepts_Only_Owner_Slash_Name(string repository, bool valid)
  {
    Assert.Equal(valid, UpdateFeed.IsValidRepository(repository));
  }

  [Fact]
  public void Latest_Picks_Highest_Newer_Version_Regardless_Of_Order()
  {
    var feed = Feed(Page("v0.2.0"), Page("v0.10.0"), Page("v0.3.0"), Page("v0.1.0"));

    var release = UpdateFeed.Latest(feed, Repo, Current);

    Assert.NotNull(release);
    Assert.Equal("v0.10.0", release.Tag);
    Assert.Equal(new Version(0, 10, 0), release.Version);
    Assert.Equal(new Uri(Page("v0.10.0")), release.PageUrl);
  }

  [Fact]
  public void Latest_Returns_Null_When_Current_Is_Newest()
  {
    Assert.Null(UpdateFeed.Latest(Feed(Page("v0.1.0"), Page("v0.0.9")), Repo, Current));
  }

  [Fact]
  public void Latest_Returns_Null_For_A_Feed_Without_Entries()
  {
    Assert.Null(UpdateFeed.Latest(Feed(), Repo, Current));
  }

  [Fact]
  public void Latest_Ignores_Other_Repositories_And_Malformed_Tags()
  {
    var feed = Feed(Page("v9.0.0", "someone/else"), Page("windows-v8.0.0"), Page("v07.0.0"), Page("v0.2.0"));

    var release = UpdateFeed.Latest(feed, Repo, Current);

    Assert.Equal("v0.2.0", release?.Tag);
  }

  [Fact]
  public void Latest_Refuses_A_Feed_With_Releases_But_No_Versioned_Tag()
  {
    var error = Assert.Throws<UpdateException>(() => UpdateFeed.Latest(Feed(Page("nightly")), Repo, Current));

    Assert.Equal("release_missing", error.Code);
  }

  [Fact]
  public void Latest_Rejects_Documents_That_Are_Not_Atom()
  {
    var error = Assert.Throws<UpdateException>(() => UpdateFeed.Latest("<rss/>"u8, Repo, Current));

    Assert.Equal("metadata_invalid", error.Code);
  }

  [Fact]
  public void Latest_Rejects_Dtd()
  {
    var feed = "<?xml version=\"1.0\"?><!DOCTYPE feed [<!ENTITY x \"y\">]><feed xmlns=\"http://www.w3.org/2005/Atom\"/>"u8.ToArray();

    var error = Assert.Throws<UpdateException>(() => UpdateFeed.Latest(feed, Repo, Current));

    Assert.Equal("metadata_invalid", error.Code);
  }

  [Fact]
  public void ReadManifest_Returns_The_Architecture_Installer_From_The_Same_Release()
  {
    var release = UpdateFeed.Latest(Feed(Page("v0.2.0")), Repo, Current)!;

    var package = UpdateFeed.ReadManifest(Manifest("0.2.0", "v0.2.0"), release, "x64");

    Assert.Equal(new Version(0, 2, 0), package.Version);
    Assert.Equal("FileWorkbench-0.2.0-x64-setup.exe", package.FileName);
    Assert.Equal(1234, package.Size);
    Assert.Equal(new Uri("https://github.com/owner/app/releases/download/v0.2.0/FileWorkbench-0.2.0-x64-setup.exe"), package.DownloadUrl);
    Assert.Equal("Fixes", package.Summary);
  }

  [Theory]
  [InlineData("0.3.0", "v0.2.0", "windows")]
  [InlineData("0.2.0", "v0.3.0", "windows")]
  [InlineData("0.2.0", "v0.2.0", "linux")]
  public void ReadManifest_Rejects_A_Manifest_That_Does_Not_Match_Its_Release(string version, string tag, string platform)
  {
    var release = UpdateFeed.Latest(Feed(Page("v0.2.0")), Repo, Current)!;

    var error = Assert.Throws<UpdateException>(() => UpdateFeed.ReadManifest(Manifest(version, tag, platform: platform), release, "x64"));

    Assert.Equal("metadata_invalid", error.Code);
  }

  [Theory]
  [InlineData("../evil.exe")]
  [InlineData("https://example.com/x.exe")]
  [InlineData("sub/dir.exe")]
  public void ReadManifest_Rejects_File_Names_With_Paths(string name)
  {
    var release = UpdateFeed.Latest(Feed(Page("v0.2.0")), Repo, Current)!;

    var error = Assert.Throws<UpdateException>(() => UpdateFeed.ReadManifest(Manifest("0.2.0", "v0.2.0", name: name), release, "x64"));

    Assert.Equal("metadata_invalid", error.Code);
  }

  [Theory]
  [InlineData("FileWorkbench-0.2.0-x64-setup.cmd")]
  [InlineData("FileWorkbench-0.3.0-x64-setup.exe")]
  [InlineData("FileWorkbench-0.2.0-x64-debug-setup.exe")]
  [InlineData("other.exe")]
  public void ReadManifest_Accepts_Only_The_Setup_Program_Of_That_Version(string name)
  {
    var release = UpdateFeed.Latest(Feed(Page("v0.2.0")), Repo, Current)!;

    var error = Assert.Throws<UpdateException>(() => UpdateFeed.ReadManifest(Manifest("0.2.0", "v0.2.0", name: name), release, "x64"));

    Assert.Equal("metadata_invalid", error.Code);
  }

  [Theory]
  [InlineData(0, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
  [InlineData(1234, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
  [InlineData(1234, "abc")]
  public void ReadManifest_Rejects_Bad_Size_Or_Digest(long size, string sha256)
  {
    var release = UpdateFeed.Latest(Feed(Page("v0.2.0")), Repo, Current)!;

    Assert.Throws<UpdateException>(() => UpdateFeed.ReadManifest(Manifest("0.2.0", "v0.2.0", size: size, sha256: sha256), release, "x64"));
  }

  [Fact]
  public void ReadManifest_Requires_The_Requested_Architecture()
  {
    var release = UpdateFeed.Latest(Feed(Page("v0.2.0")), Repo, Current)!;

    var error = Assert.Throws<UpdateException>(() => UpdateFeed.ReadManifest(Manifest("0.2.0", "v0.2.0"), release, "arm64"));

    Assert.Equal("metadata_invalid", error.Code);
  }

  [Theory]
  [InlineData("\"schemaVersion\":1", "\"schemaVersion\":\"1\"")]
  [InlineData("\"size\":1234", "\"size\":\"1234\"")]
  [InlineData("\"size\":1234", "\"size\":12.5")]
  public void ReadManifest_Rejects_Numbers_Of_The_Wrong_Type(string valid, string invalid)
  {
    var release = UpdateFeed.Latest(Feed(Page("v0.2.0")), Repo, Current)!;
    var manifest = Encoding.UTF8.GetString(Manifest("0.2.0", "v0.2.0")).Replace(valid, invalid, StringComparison.Ordinal);
    Assert.Contains(invalid, manifest, StringComparison.Ordinal);

    var error = Assert.Throws<UpdateException>(() => UpdateFeed.ReadManifest(Encoding.UTF8.GetBytes(manifest), release, "x64"));

    Assert.Equal("metadata_invalid", error.Code);
  }

  [Fact]
  public void ReadManifest_Truncates_Long_Summaries()
  {
    var release = UpdateFeed.Latest(Feed(Page("v0.2.0")), Repo, Current)!;

    var package = UpdateFeed.ReadManifest(Manifest("0.2.0", "v0.2.0", summary: new string('s', 800)), release, "x64");

    Assert.Equal(UpdateFeed.MaxSummaryLength, package.Summary.Length);
  }
}
