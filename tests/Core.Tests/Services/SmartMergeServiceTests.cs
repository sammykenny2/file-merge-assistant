using FileMergeAssistant.Models;
using FileMergeAssistant.Services;
using Xunit;

namespace FileMergeAssistant.Tests.Services;

public class SmartMergeServiceTests
{
  private readonly SmartMergeService _sut = new();

  [Fact]
  public void FindSameNameFiles_Groups_By_FileName()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("photo.jpg", "hash1", "/folder1/photo.jpg"),
      CreateFile("photo.jpg", "hash2", "/folder2/photo.jpg"),
      CreateFile("other.txt", "hash3", "/folder1/other.txt")
    };

    var groups = _sut.FindSameNameFiles(files);

    Assert.Single(groups);
    Assert.Equal("photo.jpg", groups[0].FileName);
    Assert.Equal(2, groups[0].Files.Count);
  }

  [Fact]
  public void FindSameNameFiles_Ignores_Same_Hash()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("photo.jpg", "hash1", "/folder1/photo.jpg"),
      CreateFile("photo.jpg", "hash1", "/folder2/photo.jpg")
    };

    var groups = _sut.FindSameNameFiles(files);

    Assert.Empty(groups);
  }

  [Fact]
  public void FindSameNameFiles_Returns_Empty_When_No_Matches()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("a.txt", "hash1", "/a.txt"),
      CreateFile("b.txt", "hash2", "/b.txt")
    };

    var groups = _sut.FindSameNameFiles(files);

    Assert.Empty(groups);
  }

  [Fact]
  public void FindSameNameFiles_OrderedBy_TotalSize_Descending()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("small.jpg", "h1", "/a/small.jpg", 100),
      CreateFile("small.jpg", "h2", "/b/small.jpg", 100),
      CreateFile("big.jpg", "h3", "/a/big.jpg", 10000),
      CreateFile("big.jpg", "h4", "/b/big.jpg", 10000)
    };

    var groups = _sut.FindSameNameFiles(files);

    Assert.Equal(2, groups.Count);
    Assert.Equal("big.jpg", groups[0].FileName);
  }

  private static ScannedFile CreateFile(string name, string hash, string path, long size = 1000)
  {
    return new ScannedFile
    {
      FilePath = path,
      SizeBytes = size,
      Hash = hash,
      HashAlgorithm = "sha256",
      CreatedUtc = DateTime.UtcNow,
      ModifiedUtc = DateTime.UtcNow
    };
  }
}
