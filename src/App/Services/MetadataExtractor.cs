using FileMergeAssistant.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Serilog;
using Directory = MetadataExtractor.Directory;

namespace FileMergeAssistant.Services;

public class ExifMetadataExtractor : IMetadataExtractor
{
  private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif",
    ".webp", ".heic", ".heif", ".cr2", ".nef", ".arw",
    ".mp4", ".mov", ".avi", ".mkv"
  };

  private readonly ILogger _logger;

  public ExifMetadataExtractor(ILogger logger)
  {
    _logger = logger;
  }

  public Task<ExifMetadata?> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
  {
    var extension = Path.GetExtension(filePath);
    if (!SupportedExtensions.Contains(extension))
      return Task.FromResult<ExifMetadata?>(null);

    try
    {
      var directories = ImageMetadataReader.ReadMetadata(filePath);

      DateTime? dateTaken = null;
      string? cameraModel = null;
      int? width = null;
      int? height = null;

      var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
      if (exifSubIfd != null)
      {
        if (exifSubIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
          dateTaken = dt;

        if (exifSubIfd.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var w))
          width = w;

        if (exifSubIfd.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var h))
          height = h;
      }

      var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
      if (exifIfd0 != null)
      {
        cameraModel = exifIfd0.GetDescription(ExifDirectoryBase.TagModel);

        if (width == null && exifIfd0.TryGetInt32(ExifDirectoryBase.TagImageWidth, out var w0))
          width = w0;

        if (height == null && exifIfd0.TryGetInt32(ExifDirectoryBase.TagImageHeight, out var h0))
          height = h0;
      }

      if (dateTaken == null && cameraModel == null && width == null && height == null)
        return Task.FromResult<ExifMetadata?>(null);

      return Task.FromResult<ExifMetadata?>(new ExifMetadata
      {
        DateTaken = dateTaken,
        CameraModel = cameraModel,
        Width = width,
        Height = height
      });
    }
    catch (Exception ex)
    {
      _logger.Debug(ex, "Could not extract metadata from: {FilePath}", filePath);
      return Task.FromResult<ExifMetadata?>(null);
    }
  }
}
