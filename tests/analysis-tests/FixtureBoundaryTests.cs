using System.Text.Json.Nodes;
using FileWorkbench.AnalysisVerifier;
using FileWorkbench.Tests.Scanning;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

public sealed class FixtureBoundaryTests
{
  [Theory]
  [InlineData("../escape.txt")]
  [InlineData("/rooted.txt")]
  [InlineData("C:\\rooted.txt")]
  [InlineData("a/../../escape.txt")]
  [InlineData("a//file.txt")]
  [InlineData("a./file.txt")]
  public void Rejects_Unowned_Or_Ambiguous_Path_Before_Any_Fixture_Write(string path)
  {
    using var tree = new TempTree();
    var fixture = Read();
    fixture["files"]![6]!["path"] = path;
    Assert.Throws<InvalidDataException>(() => KnownAnswerFixture.Read(fixture.ToJsonString(), tree.PathOf("new-source")));
    Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Root));
  }

  [Fact]
  public void Rejects_Empty_Fixture_And_Duplicate_Paths_Or_Ids()
  {
    using var tree = new TempTree();
    var fixture = Read();
    fixture["files"] = new JsonArray();
    Assert.Throws<InvalidDataException>(() => KnownAnswerFixture.Read(fixture.ToJsonString(), tree.PathOf("source")));
    fixture = Read();
    fixture["files"]![6]!["path"] = fixture["files"]![0]!["path"]!.GetValue<string>();
    Assert.Throws<InvalidDataException>(() => KnownAnswerFixture.Read(fixture.ToJsonString(), tree.PathOf("source")));
    fixture = Read();
    fixture["files"]![6]!["id"] = fixture["files"]![0]!["id"]!.GetValue<long>();
    Assert.Throws<InvalidDataException>(() => KnownAnswerFixture.Read(fixture.ToJsonString(), tree.PathOf("source")));
    Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Root));
  }

  [Fact]
  public void Canonical_Fixture_Has_Seven_Distinct_Files_And_Two_Verified_Groups()
  {
    using var tree = new TempTree();
    var fixture = KnownAnswerFixture.Read(Read().ToJsonString(), tree.PathOf("source"));
    Assert.Equal(7, fixture.Files.Length);
    Assert.Equal(2, fixture.Groups.Length);
    Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Root));
  }

  private static JsonObject Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "analysis", "known-answer.json")))!.AsObject();
}
