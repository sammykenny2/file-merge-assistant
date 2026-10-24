using FileWorkbench.Models;
using FileWorkbench.Services;
using NSubstitute;
using Xunit;

namespace FileWorkbench.Tests.Services;

public class DuplicateDetectorExtendedTests
{
  private readonly DuplicateDetector _sut = new();

  [Fact]
  public void FindDuplicatesBySize_Groups_By_Size_Then_Hash()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("/a/file1.txt", "hash_a", 1000),
      CreateFile("/b/file2.txt", "hash_a", 1000),
      CreateFile("/c/file3.txt", "hash_b", 2000),
    };

    var result = _sut.FindDuplicatesBySize(files);

    Assert.Single(result);
    Assert.Equal("hash_a", result[0].Hash);
  }

  [Fact]
  public void FindDuplicatesBySize_Skips_Unique_Sizes()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("/a/file1.txt", "hash_a", 1000),
      CreateFile("/b/file2.txt", "hash_a", 2000), // different size, same hash
    };

    var result = _sut.FindDuplicatesBySize(files);

    Assert.Empty(result); // Different sizes, no grouping
  }

  [Fact]
  public void FindDuplicatesByName_Groups_By_Filename()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("/folder1/photo.jpg", "hash_a", 1000),
      CreateFile("/folder2/photo.jpg", "hash_b", 2000),
      CreateFile("/folder3/unique.txt", "hash_c", 500),
    };

    var result = _sut.FindDuplicatesByName(files);

    Assert.Single(result);
    Assert.Equal(2, result[0].Files.Count);
  }

  [Fact]
  public void FindDuplicatesByName_Case_Insensitive()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("/folder1/Photo.JPG", "hash_a", 1000),
      CreateFile("/folder2/photo.jpg", "hash_b", 1000),
    };

    var result = _sut.FindDuplicatesByName(files);

    Assert.Single(result);
  }

  [Fact]
  public void FindDuplicatesByNameAndSize_Groups_By_Both()
  {
    var files = new List<ScannedFile>
    {
      CreateFile("/folder1/doc.pdf", "hash_a", 1000),
      CreateFile("/folder2/doc.pdf", "hash_b", 1000),
      CreateFile("/folder3/doc.pdf", "hash_c", 2000), // different size
    };

    var result = _sut.FindDuplicatesByNameAndSize(files);

    Assert.Single(result);
    Assert.Equal(2, result[0].Files.Count);
  }

  [Fact]
  public async Task VerifyGroupsByteByByte_Removes_Non_Matching_Files()
  {
    var verifier = Substitute.For<IByteByByteVerifier>();
    verifier.AreFilesIdenticalAsync("/a/f1.txt", "/b/f2.txt", Arg.Any<CancellationToken>())
      .Returns(true);
    verifier.AreFilesIdenticalAsync("/a/f1.txt", "/c/f3.txt", Arg.Any<CancellationToken>())
      .Returns(false);

    var groups = new List<DuplicateGroup>
    {
      new()
      {
        Hash = "hash_a",
        Files = new List<ScannedFile>
        {
          CreateFile("/a/f1.txt", "hash_a", 100),
          CreateFile("/b/f2.txt", "hash_a", 100),
          CreateFile("/c/f3.txt", "hash_a", 100),
        }
      }
    };

    var result = await _sut.VerifyGroupsByteByByteAsync(groups, verifier);

    Assert.Single(result);
    Assert.Equal(2, result[0].Files.Count);
  }

  [Fact]
  public async Task VerifyGroupsByteByByte_Eliminates_Group_When_No_Match()
  {
    var verifier = Substitute.For<IByteByByteVerifier>();
    verifier.AreFilesIdenticalAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
      .Returns(false);

    var groups = new List<DuplicateGroup>
    {
      new()
      {
        Hash = "hash_a",
        Files = new List<ScannedFile>
        {
          CreateFile("/a/f1.txt", "hash_a", 100),
          CreateFile("/b/f2.txt", "hash_a", 100),
        }
      }
    };

    var result = await _sut.VerifyGroupsByteByByteAsync(groups, verifier);

    Assert.Empty(result);
  }

  [Fact]
  public void FilterHardlinks_Removes_Hardlinked_Pairs()
  {
    var hardlinkDetector = Substitute.For<IHardlinkDetector>();
    hardlinkDetector.AreHardlinked("/a/f1.txt", "/b/f2.txt").Returns(true);
    hardlinkDetector.AreHardlinked("/a/f1.txt", "/c/f3.txt").Returns(false);

    var groups = new List<DuplicateGroup>
    {
      new()
      {
        Hash = "hash_a",
        Files = new List<ScannedFile>
        {
          CreateFile("/a/f1.txt", "hash_a", 100),
          CreateFile("/b/f2.txt", "hash_a", 100),
          CreateFile("/c/f3.txt", "hash_a", 100),
        }
      }
    };

    var result = _sut.FilterHardlinks(groups, hardlinkDetector);

    Assert.Single(result);
    Assert.Equal(2, result[0].Files.Count); // f1 and f3 remain
  }

  [Fact]
  public void FilterHardlinks_Eliminates_All_Hardlinked_Group()
  {
    var hardlinkDetector = Substitute.For<IHardlinkDetector>();
    hardlinkDetector.AreHardlinked(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

    var groups = new List<DuplicateGroup>
    {
      new()
      {
        Hash = "hash_a",
        Files = new List<ScannedFile>
        {
          CreateFile("/a/f1.txt", "hash_a", 100),
          CreateFile("/b/f2.txt", "hash_a", 100),
        }
      }
    };

    var result = _sut.FilterHardlinks(groups, hardlinkDetector);

    Assert.Empty(result);
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
