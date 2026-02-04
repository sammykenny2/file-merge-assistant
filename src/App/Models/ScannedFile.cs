namespace FileMergeAssistant.Models;

public record ScannedFile
{
  public required string FilePath { get; init; }
  public required long SizeBytes { get; init; }
  public required DateTime CreatedUtc { get; init; }
  public required DateTime ModifiedUtc { get; init; }
  public string? Hash { get; init; }
  public string? HashAlgorithm { get; init; }
  public ExifMetadata? Metadata { get; init; }
}

public record ExifMetadata
{
  public DateTime? DateTaken { get; init; }
  public string? CameraModel { get; init; }
  public int? Width { get; init; }
  public int? Height { get; init; }
}
