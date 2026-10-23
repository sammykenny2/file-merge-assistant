using FileMergeAssistant.Scanning;
using Xunit;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class AnalysisBaselineTests
{
  [Fact]
  public void Legacy_Content_Fingerprints_Match_Independent_Known_Groups_Without_Modifying_Sources()
  {
    using var dataset = new KnownAnswerDataset();
    var before = dataset.Files.ToDictionary(file => file.Id, file => File.GetLastWriteTimeUtc(file.Path));
    var hashes = dataset.Files.Select(file => (file.Id, Hash: SourceScanner.Fingerprint(file.Path, CancellationToken.None))).ToList();
    var groups = hashes.GroupBy(file => file.Hash, StringComparer.Ordinal).Where(group => group.Count() > 1)
      .Select(group => string.Join(',', group.Select(file => file.Id).Order())).Order(StringComparer.Ordinal);

    Assert.Equal(dataset.ExactGroups.Select(group => string.Join(',', group)).Order(StringComparer.Ordinal), groups);
    Assert.Equal(7, hashes.Count);
    Assert.All(dataset.Files, file =>
    {
      Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path));
      Assert.Equal(before[file.Id], File.GetLastWriteTimeUtc(file.Path));
    });
  }
}
