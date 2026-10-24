using FileWorkbench.Models;
using FileWorkbench.Services;
using Xunit;

namespace FileWorkbench.Tests.Services;

public class DuplicateDetectorTests
{
  private readonly DuplicateDetector _sut = new();

  [Fact]
  public void FindDuplicates_No_Files_Returns_Empty()
  {
    var result = _sut.FindDuplicates([]);

    Assert.Empty(result);
  }

  [Fact]
  public void FindDuplicates_No_Duplicates_Returns_Empty()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("file1.txt", "hash_a", 100),
      CreateFile("file2.txt", "hash_b", 200),
      CreateFile("file3.txt", "hash_c", 300),
    };

    var result = _sut.FindDuplicates(files);

    Assert.Empty(result);
  }

  [Fact]
  public void FindDuplicates_Groups_By_Hash()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("file1.txt", "hash_a", 100),
      CreateFile("file2.txt", "hash_a", 100),
      CreateFile("file3.txt", "hash_b", 200),
    };

    var result = _sut.FindDuplicates(files);

    Assert.Single(result);
    Assert.Equal("hash_a", result[0].Hash);
    Assert.Equal(2, result[0].Files.Count);
  }

  [Fact]
  public void FindDuplicates_Multiple_Groups()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("a1.txt", "hash_a", 100),
      CreateFile("a2.txt", "hash_a", 100),
      CreateFile("b1.txt", "hash_b", 500),
      CreateFile("b2.txt", "hash_b", 500),
      CreateFile("b3.txt", "hash_b", 500),
      CreateFile("unique.txt", "hash_c", 50),
    };

    var result = _sut.FindDuplicates(files);

    Assert.Equal(2, result.Count);
    // Ordered by wasted bytes descending
    Assert.Equal("hash_b", result[0].Hash);
    Assert.Equal(3, result[0].Files.Count);
    Assert.Equal("hash_a", result[1].Hash);
  }

  [Fact]
  public void FindDuplicates_Calculates_WastedBytes()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("file1.txt", "hash_a", 1000),
      CreateFile("file2.txt", "hash_a", 1000),
      CreateFile("file3.txt", "hash_a", 1000),
    };

    var result = _sut.FindDuplicates(files);

    Assert.Single(result);
    Assert.Equal(2000, result[0].WastedBytes);
  }

  [Fact]
  public void FindDuplicates_Skips_Null_Hash()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("file1.txt", null, 100),
      CreateFile("file2.txt", null, 200),
    };

    var result = _sut.FindDuplicates(files);

    Assert.Empty(result);
  }

  [Fact]
  public void FindDuplicates_Orders_Files_By_CreatedUtc()
  {
    var now = DateTime.UtcNow;
    var files = new List<ScannedFile>
    {
      new()
      {
        FilePath = "newer.txt", Hash = "hash_a", SizeBytes = 100,
        CreatedUtc = now, ModifiedUtc = now
      },
      new()
      {
        FilePath = "older.txt", Hash = "hash_a", SizeBytes = 100,
        CreatedUtc = now.AddDays(-1), ModifiedUtc = now
      },
    };

    var result = _sut.FindDuplicates(files);

    Assert.Equal("older.txt", result[0].Files[0].FilePath);
    Assert.Equal("newer.txt", result[0].Files[1].FilePath);
  }

  private static ScannedFile CreateFile(string path, string? hash, long size)
  {
    var now = DateTime.UtcNow;
    return new ScannedFile
    {
      FilePath = path,
      Hash = hash,
      HashAlgorithm = "sha256",
      SizeBytes = size,
      CreatedUtc = now,
      ModifiedUtc = now
    };
  }
}
