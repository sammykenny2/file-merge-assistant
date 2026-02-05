using System.Globalization;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FileMergeAssistant.Hashing;

public class PerceptualHashAlgorithm : IHashAlgorithm
{
  private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp"
  };

  private readonly AverageHash _averageHash = new();

  public string Name => "perceptual";

  public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
  {
    var extension = Path.GetExtension(filePath);
    if (!SupportedExtensions.Contains(extension))
    {
      throw new NotSupportedException(
        $"Perceptual hashing is only supported for image files. Unsupported extension: {extension}");
    }

    using var image = await Image.LoadAsync<Rgba32>(filePath, cancellationToken);
    var hash = _averageHash.Hash(image);
    return hash.ToString("x16", CultureInfo.InvariantCulture);
  }
}
