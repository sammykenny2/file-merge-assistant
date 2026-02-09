using FileMergeAssistant.Models;
using FileMergeAssistant.Resolution;
using Xunit;

namespace FileMergeAssistant.Tests.Resolution;

public class MetadataResolutionTests
{
  private readonly ResolutionService _sut = new();

  [Fact]
  public void AutoResolveByMetadata_Prefers_File_With_Metadata()
  {
    var now = DateTime.UtcNow;
    var group = new DuplicateGroup
    {
      Hash = "abc123",
      Files =
      [
        new ScannedFile
        {
          FilePath = "no-meta.jpg",
          SizeBytes = 1000,
          CreatedUtc = now,
          ModifiedUtc = now,
          Hash = "abc123",
          HashAlgorithm = "sha256",
          Metadata = null
        },
        new ScannedFile
        {
          FilePath = "with-meta.jpg",
          SizeBytes = 1000,
          CreatedUtc = now.AddDays(-1),
          ModifiedUtc = now,
          Hash = "abc123",
          HashAlgorithm = "sha256",
          Metadata = new ExifMetadata
          {
            DateTaken = now.AddDays(-10),
            CameraModel = "Canon EOS R5",
            Width = 8192,
            Height = 5464
          }
        }
      ]
    };

    var decisions = _sut.AutoResolveByMetadata([group]);

    Assert.Single(decisions);
    Assert.Equal("with-meta.jpg", decisions[0].KeptFilePath);
  }

  [Fact]
  public void AutoResolveByMetadata_Prefers_Higher_Resolution()
  {
    var now = DateTime.UtcNow;
    var group = new DuplicateGroup
    {
      Hash = "abc123",
      Files =
      [
        new ScannedFile
        {
          FilePath = "low-res.jpg",
          SizeBytes = 500,
          CreatedUtc = now,
          ModifiedUtc = now,
          Hash = "abc123",
          HashAlgorithm = "sha256",
          Metadata = new ExifMetadata { Width = 640, Height = 480 }
        },
        new ScannedFile
        {
          FilePath = "high-res.jpg",
          SizeBytes = 2000,
          CreatedUtc = now,
          ModifiedUtc = now,
          Hash = "abc123",
          HashAlgorithm = "sha256",
          Metadata = new ExifMetadata
          {
            Width = 4000,
            Height = 3000,
            CameraModel = "Sony A7III",
            DateTaken = now.AddDays(-5)
          }
        }
      ]
    };

    var decisions = _sut.AutoResolveByMetadata([group]);

    Assert.Equal("high-res.jpg", decisions[0].KeptFilePath);
  }

  [Fact]
  public void AutoResolveByFolderPriority_Keeps_File_In_Priority_Folder()
  {
    var now = DateTime.UtcNow;
    var group = new DuplicateGroup
    {
      Hash = "abc123",
      Files =
      [
        new ScannedFile
        {
          FilePath = "/backup/photo.jpg",
          SizeBytes = 1000,
          CreatedUtc = now,
          ModifiedUtc = now,
          Hash = "abc123",
          HashAlgorithm = "sha256"
        },
        new ScannedFile
        {
          FilePath = "/originals/photo.jpg",
          SizeBytes = 1000,
          CreatedUtc = now,
          ModifiedUtc = now,
          Hash = "abc123",
          HashAlgorithm = "sha256"
        }
      ]
    };

    var priorities = new List<FolderPriority>
    {
      new() { FolderPath = "/originals", Priority = 100 },
      new() { FolderPath = "/backup", Priority = 10 }
    };

    var decisions = _sut.AutoResolveByFolderPriority([group], priorities);

    Assert.Equal("/originals/photo.jpg", decisions[0].KeptFilePath);
  }

  [Fact]
  public void AutoResolveByMetadata_Empty_Groups_Returns_Empty()
  {
    var decisions = _sut.AutoResolveByMetadata([]);
    Assert.Empty(decisions);
  }
}
