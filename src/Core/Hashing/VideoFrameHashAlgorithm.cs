using System.Diagnostics;
using System.Globalization;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FileMergeAssistant.Hashing;

public class VideoFrameHashAlgorithm : IHashAlgorithm
{
  private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg"
  };

  private readonly AverageHash _averageHash = new();

  public string Name => "video-perceptual";

  public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
  {
    var extension = Path.GetExtension(filePath);
    if (!SupportedExtensions.Contains(extension))
    {
      throw new NotSupportedException(
        $"Video perceptual hashing is only supported for video files. Unsupported extension: {extension}");
    }

    // Extract a keyframe from the video using FFmpeg
    var tempImagePath = Path.Combine(Path.GetTempPath(), $"fma-frame-{Guid.NewGuid():N}.png");

    try
    {
      var extracted = await ExtractKeyframeAsync(filePath, tempImagePath, cancellationToken);
      if (!extracted || !File.Exists(tempImagePath))
      {
        throw new InvalidOperationException(
          $"Could not extract keyframe from video: {filePath}. Ensure FFmpeg is installed and available in PATH.");
      }

      using var image = await Image.LoadAsync<Rgba32>(tempImagePath, cancellationToken);
      var hash = _averageHash.Hash(image);
      return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
    finally
    {
      if (File.Exists(tempImagePath))
        File.Delete(tempImagePath);
    }
  }

  private static async Task<bool> ExtractKeyframeAsync(
    string videoPath, string outputPath, CancellationToken cancellationToken)
  {
    try
    {
      using var process = new Process();
      process.StartInfo = new ProcessStartInfo
      {
        FileName = "ffmpeg",
        Arguments = $"-i \"{videoPath}\" -vf \"select=eq(pict_type\\,I)\" -frames:v 1 -y \"{outputPath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      process.Start();

      // Read output asynchronously to prevent deadlock
      var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken);
      await errorTask;

      return process.ExitCode == 0 && File.Exists(outputPath);
    }
    catch (Exception) when (!cancellationToken.IsCancellationRequested)
    {
      // FFmpeg not available - fall back to thumbnail at 1 second
      return await ExtractSimpleFrameAsync(videoPath, outputPath, cancellationToken);
    }
  }

  private static async Task<bool> ExtractSimpleFrameAsync(
    string videoPath, string outputPath, CancellationToken cancellationToken)
  {
    try
    {
      using var process = new Process();
      process.StartInfo = new ProcessStartInfo
      {
        FileName = "ffmpeg",
        Arguments = $"-ss 00:00:01 -i \"{videoPath}\" -frames:v 1 -y \"{outputPath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      process.Start();
      var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken);
      await errorTask;

      return process.ExitCode == 0 && File.Exists(outputPath);
    }
    catch
    {
      return false;
    }
  }
}
